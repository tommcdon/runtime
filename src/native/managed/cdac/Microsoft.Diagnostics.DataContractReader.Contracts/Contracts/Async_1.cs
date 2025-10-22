// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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
    private readonly IRuntimeTypeSystem _rts;
    private readonly IThread _thread;
    private readonly IEcmaMetadata _ecmaMetadata;
    private readonly IPrecodeStubs _precodeStubs;
    private readonly IDebugInfo _debugInfo;

    public Async_1(Target target)
    {
        _target = target;
        _loader = target.Contracts.Loader;
        _rts = target.Contracts.RuntimeTypeSystem;
        _thread = target.Contracts.Thread;
        _ecmaMetadata = target.Contracts.EcmaMetadata;
        _precodeStubs = target.Contracts.PrecodeStubs;
        _debugInfo = target.Contracts.DebugInfo;
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

    private TargetPointer GetTLSNextContinuationDataAddr(TargetPointer threadPtr)
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

                Debug.Assert(mdReader.GetString(fieldDef.Name) == "t_nextContinuation");

                uint fieldOffset = _rts.GetFieldDescOffset(fieldPointer, fieldDef);

                return _target.ReadPointer(baseAddr + fieldOffset);
            }
        }

        return TargetPointer.Null;
    }

    private TargetPointer GetFinalResumeIP(Data.Continuation continuation)
    {
        if (continuation.Resume == TargetPointer.Null)
            return TargetPointer.Null;

        TargetPointer ilStubMD = _precodeStubs.GetMethodDescFromStubAddress(continuation.Resume.Value);
        MethodDescHandle ilStubHandle = _rts.GetMethodDescHandle(ilStubMD);
        TargetPointer resolverPtr = _rts.GetResolver(ilStubHandle);
        AsyncResumeILStubResolver resolver = _target.ProcessedData.GetOrAdd<AsyncResumeILStubResolver>(resolverPtr);
        return resolver.FinalResumeIP;
    }

    private IEnumerable<ResumeData> ReadAsyncStack(TargetPointer continuationPtr)
    {
        while (continuationPtr != TargetPointer.Null)
        {
            Continuation continuation = _target.ProcessedData.GetOrAdd<Continuation>(continuationPtr);

            if (continuation.Resume != TargetPointer.Null)
            {
                TargetPointer ilStubMD = _precodeStubs.GetMethodDescFromStubAddress(continuation.Resume.Value);
                MethodDescHandle ilStubHandle = _rts.GetMethodDescHandle(ilStubMD);
                TargetPointer resolvedMD = _rts.GetILStubTargetMethodDesc(ilStubHandle);
                MethodDescHandle methodDescHandle = _rts.GetMethodDescHandle(resolvedMD);
                TargetCodePointer finalResumeIP = GetFinalResumeIP(continuation).Value;
                AsyncSuspensionPoint[] suspensionPoints = _debugInfo.GetAsyncSuspensionPoints(finalResumeIP).ToArray();
                if (suspensionPoints.Length <= continuation.State)
                    throw new InvalidOperationException("Invalid continuation state index.");

                yield return new ResumeData(
                    methodDescHandle,
                    finalResumeIP,
                    suspensionPoints[continuation.State].NativeOffset,
                    suspensionPoints[continuation.State].NumContinuationVars);
            }

            continuationPtr = continuation.Next;
        }
    }

    IEnumerable<IEnumerable<ResumeData>> IAsync.GetAsyncData(TargetPointer thread)
    {
        TargetPointer tlsNextContinuationAddr = GetTLSNextContinuationDataAddr(thread);

        while (tlsNextContinuationAddr != TargetPointer.Null)
        {
            NextContinuationData nextContinuationData = _target.ProcessedData.GetOrAdd<NextContinuationData>(tlsNextContinuationAddr);
            if (nextContinuationData.NextContinuation != TargetPointer.Null)
            {
                // nextContinuationData.NextContinuation is a pointer to a pointer to a Continuation object
                // so we need to dereference it again to get the Continuation pointer
                TargetPointer continuationPtr = _target.ReadPointer(nextContinuationData.NextContinuation);
                yield return ReadAsyncStack(continuationPtr);
            }

            tlsNextContinuationAddr = nextContinuationData.Next;
        }
    }
}
