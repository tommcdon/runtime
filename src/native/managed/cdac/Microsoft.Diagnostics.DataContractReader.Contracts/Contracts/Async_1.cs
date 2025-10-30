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

    private void GetMethodLocals(MethodDescHandle mdh)
    {
        uint token = _rts.GetMethodToken(mdh);
        TypeHandle type = _rts.GetTypeHandle(_rts.GetMethodTable(mdh));
        TargetPointer modulePtr = _rts.GetModule(type);
        ModuleHandle moduleHandle = _loader.GetModuleHandleFromModulePtr(modulePtr);

        MethodDefinitionHandle methodHandle = (MethodDefinitionHandle)MetadataTokens.Handle((int)token);
        MetadataReader mdReader = _ecmaMetadata.GetMetadata(moduleHandle)!;
        MethodDefinition _ = mdReader.GetMethodDefinition(methodHandle);
        throw new NotImplementedException();
    }

    private static IEnumerable<AsyncLocal> GetLocals(
        AsyncSuspensionPoint[] asyncSuspensionPoints,
        AsyncVarInfo[] asyncVars,
        Continuation continuation)
    {
        uint varBeginIndex = 0;
        for (int i = 0; i < continuation.State; i++)
        {
            varBeginIndex += asyncSuspensionPoints[i].NumContinuationVars;
        }

        AsyncSuspensionPoint asp = asyncSuspensionPoints[continuation.State];
        uint numVars = asp.NumContinuationVars;

        for (int i = 0; i < numVars; i++)
        {
            AsyncVarInfo avi = asyncVars[varBeginIndex + i];
            yield return new AsyncLocal(avi.VarNumber, continuation.Address + avi.Offset);
        }
    }

    private IEnumerable<ResumeData> ReadAsyncStack(TargetPointer continuationPtr)
    {
        while (continuationPtr != TargetPointer.Null)
        {
            Continuation continuation = _target.ProcessedData.GetOrAdd<Continuation>(continuationPtr);

            if (continuation.ResumeInfo != TargetPointer.Null)
            {
                ResumeInfo resumeInfo = _target.ProcessedData.GetOrAdd<ResumeInfo>(continuation.ResumeInfo);
                CodeBlockHandle? cbh = _eman.GetCodeBlockHandle(resumeInfo.Resume.Value);
                if (!cbh.HasValue)
                    continue;
                TargetPointer pMethodDesc = _eman.GetMethodDesc(cbh.Value);
                MethodDescHandle mdh = _rts.GetMethodDescHandle(pMethodDesc);
                TargetCodePointer codeStart = _rts.GetNativeCode(mdh);
                AsyncSuspensionPoint[] suspensionPoints = [.. _debugInfo.GetAsyncSuspensionPoints(codeStart)];
                AsyncVarInfo[] asyncVars = [.. _debugInfo.GetAsyncVarInfo(codeStart)];
                if (suspensionPoints.Length <= continuation.State)
                    throw new InvalidOperationException("Invalid continuation state index.");

                uint token = _rts.GetMethodToken(mdh);
                TypeHandle type = _rts.GetTypeHandle(_rts.GetMethodTable(mdh));
                TargetPointer modulePtr = _rts.GetModule(type);
                ModuleHandle moduleHandle = _loader.GetModuleHandleFromModulePtr(modulePtr);
                yield return new ResumeData(
                    moduleHandle,
                    token,
                    mdh,
                    codeStart,
                    suspensionPoints[continuation.State].NativeDiagnosticsOffset,
                    GetLocals(suspensionPoints, asyncVars, continuation));
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

    public ImmutableArray<TypeHandle> ParseLocal(ResumeData rd)
    {
        TargetPointer mtAddr = _rts.GetMethodTable(rd.MethodDesc);
        TypeHandle typeHandle = _rts.GetTypeHandle(mtAddr);
        TargetPointer modulePtr = _rts.GetModule(typeHandle);
        ModuleHandle moduleHandle = _loader.GetModuleHandleFromModulePtr(modulePtr);

        if (_ecmaMetadata.GetMetadata(moduleHandle) is not MetadataReader mdReader)
            throw new InvalidOperationException("Metadata not found.");

        uint token = _rts.GetMethodToken(rd.MethodDesc);
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
}
