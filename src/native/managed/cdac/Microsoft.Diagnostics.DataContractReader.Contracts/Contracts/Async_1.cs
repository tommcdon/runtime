// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Microsoft.Diagnostics.DataContractReader.Data;

namespace Microsoft.Diagnostics.DataContractReader.Contracts;

internal record ResumeData_1(
    TargetCodePointer ResumePoint,
    TargetCodePointer DiagnosticIP,
    Continuation Continuation
) : ResumeData(ResumePoint, DiagnosticIP);

internal readonly partial struct Async_1 : IAsync
{
    private readonly Target _target;
    private readonly ILoader _loader;
    private readonly IExecutionManager _eman;
    private readonly IRuntimeTypeSystem _rts;
    private readonly IEcmaMetadata _ecmaMetadata;
    private readonly IDebugInfo _debugInfo;
    private readonly ISignatureDecoder _signatureDecoder;

    public Async_1(Target target)
    {
        _target = target;
        _loader = target.Contracts.Loader;
        _eman = target.Contracts.ExecutionManager;
        _rts = target.Contracts.RuntimeTypeSystem;
        _ecmaMetadata = target.Contracts.EcmaMetadata;
        _debugInfo = target.Contracts.DebugInfo;
        _signatureDecoder = target.Contracts.SignatureDecoder;
    }

    private bool TryGetTypeByName(string typeName, string typeNamespace, out TypeHandle typeHandle, out ModuleHandle moduleHandle)
    {
        typeHandle = default;
        moduleHandle = default;

        TargetPointer appDomainPointer = _target.ReadGlobalPointer(Constants.Globals.AppDomain);
        TargetPointer appDomain = _target.ReadPointer(appDomainPointer);

        foreach (ModuleHandle module in _loader.GetModuleHandles(
            appDomain,
            AssemblyIterationFlags.IncludeLoaded | AssemblyIterationFlags.IncludeExecution))
        {
            TypeHandle type = _rts.GetTypeByNameAndModule(typeName, typeNamespace, module);
            if (!type.IsNull)
            {
                typeHandle = type;
                moduleHandle = module;
                return true;
            }
        }
        return false;
    }

    private TargetPointer GetTLSDispatcherInfoAddr(TargetPointer threadPtr)
    {
        if (!TryGetTypeByName(
            "AsyncHelpers+RuntimeAsyncTaskCore",
            "System.Runtime.CompilerServices",
            out TypeHandle typeHandle,
            out ModuleHandle moduleHandle))
        {
            throw new InvalidOperationException("Type AsyncHelpers+RuntimeAsyncTaskCore not found in any loaded module.");
        }

        foreach (TargetPointer fieldPointer in _rts.GetFieldDescs(typeHandle))
        {
            if (_rts.IsFieldDescThreadStatic(fieldPointer))
            {
                CorElementType fieldType = _rts.GetFieldDescType(fieldPointer);
                bool isGCStatic = fieldType == CorElementType.ValueType || fieldType == CorElementType.Class;

                TargetPointer baseAddr = isGCStatic ? _rts.GetGCThreadStaticsBasePointer(typeHandle, threadPtr) : _rts.GetNonGCThreadStaticsBasePointer(typeHandle, threadPtr);

                uint token = _rts.GetFieldDescMemberDef(fieldPointer);
                FieldDefinitionHandle fieldHandle = (FieldDefinitionHandle)MetadataTokens.Handle((int)token);
                MetadataReader mdReader = _ecmaMetadata.GetMetadata(moduleHandle)!;
                FieldDefinition fieldDef = mdReader.GetFieldDefinition(fieldHandle);

                Debug.Assert(mdReader.GetString(fieldDef.Name) == "t_dispatcherInfo");

                uint fieldOffset = _rts.GetFieldDescOffset(fieldPointer, fieldDef);

                return _target.ReadPointer(baseAddr + fieldOffset);
            }
        }

        return TargetPointer.Null;
    }

    private IEnumerable<ResumeData> ReadAsyncStack(TargetPointer continuationPtr)
    {
        while (continuationPtr != TargetPointer.Null)
        {
            Continuation continuation = _target.ProcessedData.GetOrAdd<Continuation>(continuationPtr);

            if (continuation.ResumeInfo != TargetPointer.Null)
            {
                ResumeInfo resumeInfo = _target.ProcessedData.GetOrAdd<ResumeInfo>(continuation.ResumeInfo);
                yield return new ResumeData_1(
                    resumeInfo.Resume,
                    resumeInfo.DiagnosticIP,
                    continuation);
            }

            continuationPtr = continuation.Next;
        }
    }

    IEnumerable<IEnumerable<ResumeData>> IAsync.GetAsyncData(TargetPointer thread)
    {
        TargetPointer tlsDispatcherInfoAddr = GetTLSDispatcherInfoAddr(thread);

        while (tlsDispatcherInfoAddr != TargetPointer.Null)
        {
            DispatcherInfo dispatcherInfo = _target.ProcessedData.GetOrAdd<DispatcherInfo>(tlsDispatcherInfoAddr);
            if (dispatcherInfo.NextContinuation != TargetPointer.Null)
            {
                yield return ReadAsyncStack(dispatcherInfo.NextContinuation);
            }

            tlsDispatcherInfoAddr = dispatcherInfo.Next;
        }
    }

    IEnumerable<AsyncLocal> IAsync.GetLocals(ResumeData data)
    {
        ResumeData_1 rd = AssertCorrectResumeData(data);

        AsyncSuspensionPoint[] suspensionPoints = [.. _debugInfo.GetAsyncSuspensionPoints(rd.DiagnosticIP)];
        AsyncVarInfo[] asyncVars = [.. _debugInfo.GetAsyncVarInfo(rd.DiagnosticIP)];
        if (suspensionPoints.Length <= rd.Continuation.State)
            throw new InvalidOperationException("Invalid continuation state index.");


        uint varBeginIndex = 0;
        for (int i = 0; i < rd.Continuation.State; i++)
            varBeginIndex += suspensionPoints[i].NumContinuationVars;

        AsyncSuspensionPoint asp = suspensionPoints[rd.Continuation.State];
        uint numVars = asp.NumContinuationVars;
        for (int i = 0; i < numVars; i++)
        {
            AsyncVarInfo avi = asyncVars[varBeginIndex + i];
            yield return new AsyncLocal(avi.VarNumber, rd.Continuation.Address + avi.Offset);
        }
    }

    private TypeHandle[] ParseILVarTypes(ResumeData_1 rd)
    {
        if (_eman.GetCodeBlockHandle(rd.DiagnosticIP) is not CodeBlockHandle cbh)
            return [];

        TargetPointer pMethodDesc = _eman.GetMethodDesc(cbh);
        MethodDescHandle md = _rts.GetMethodDescHandle(pMethodDesc);
        TargetPointer mtAddr = _rts.GetMethodTable(md);
        TypeHandle typeHandle = _rts.GetTypeHandle(mtAddr);
        TargetPointer modulePtr = _rts.GetModule(typeHandle);
        ModuleHandle moduleHandle = _loader.GetModuleHandleFromModulePtr(modulePtr);

        if (_ecmaMetadata.GetMetadata(moduleHandle) is not MetadataReader mdReader)
            throw new InvalidOperationException("Metadata not found.");

        uint token = _rts.GetMethodToken(md);
        MethodDefinitionHandle methodDefHandle = (MethodDefinitionHandle)MetadataTokens.Handle((int)token);
        MethodDefinition methodDef = mdReader.GetMethodDefinition(methodDefHandle);
        MethodSignature<TypeHandle> methodSig = methodDef.DecodeSignature(_signatureDecoder.GetTypeHandleProvider(moduleHandle), typeHandle);

        // Only fat headers have local var sigs
        ImmutableArray<TypeHandle> localTypes = [];
        TargetPointer ilHeader = _loader.GetILHeader(moduleHandle, token);
        if (ilHeader != TargetPointer.Null)
        {
            if (HeaderReaderHelpers.TryGetLocalVarSigToken(_target, ilHeader, out int localVarSigToken))
            {
                StandaloneSignatureHandle localSignatureHandle = (StandaloneSignatureHandle)MetadataTokens.Handle(localVarSigToken);
                StandaloneSignature sig = mdReader.GetStandaloneSignature(localSignatureHandle);
                localTypes = sig.DecodeLocalSignature(_signatureDecoder.GetTypeHandleProvider(moduleHandle), typeHandle);
            }
        }

        // Order in this array should match up with the ILVarNum in AsyncLocal
        return [.. methodSig.ParameterTypes, .. localTypes];
    }

    bool IAsync.TryGetLocalType(ResumeData data, uint ilVarNum, out TypeHandle typeHandle)
    {
        typeHandle = default;

        ResumeData_1 rd = AssertCorrectResumeData(data);

        try
        {
            TypeHandle[] typeHandles = ParseILVarTypes(rd);
            if (ilVarNum < typeHandles.Length)
            {
                typeHandle = typeHandles[ilVarNum];
                return true;
            }
        }
        catch (System.Exception)
        {
            // Ignore errors
        }

        return false;
    }

    private static ResumeData_1 AssertCorrectResumeData(ResumeData rd)
    {
        if (rd is not ResumeData_1 rd1)
            throw new InvalidOperationException("Invalid ResumeData for contract version");

        return rd1;
    }
}
