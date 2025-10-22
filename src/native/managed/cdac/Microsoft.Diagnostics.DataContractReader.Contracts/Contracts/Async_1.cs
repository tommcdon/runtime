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

    public Async_1(Target target)
    {
        _target = target;
        _loader = target.Contracts.Loader;
        _rts = target.Contracts.RuntimeTypeSystem;
        _thread = target.Contracts.Thread;
        _ecmaMetadata = target.Contracts.EcmaMetadata;
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

    private string ReadAsyncStack(Data.Continuation continuation)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine($"  Continuation IP: {continuation.Resume}, State: {continuation.State}, Flags: {continuation.Flags}");

        if (continuation.Next != TargetPointer.Null)
        {
            Continuation parent = _target.ProcessedData.GetOrAdd<Continuation>(continuation.Next);
            ReadAsyncStack(parent);
        }

        string message = sb.ToString();
        return message;
    }

    private string ReadAllAsyncStacks(TargetPointer nextContinuationDataPtr)
    {
        StringBuilder sb = new StringBuilder();

        while (nextContinuationDataPtr != TargetPointer.Null)
        {
            NextContinuationData nextContinuationData = _target.ProcessedData.GetOrAdd<NextContinuationData>(nextContinuationDataPtr);

            if (nextContinuationData.NextContinuation != TargetPointer.Null)
            {
                TargetPointer continuationPtr = _target.ReadPointer(nextContinuationData.NextContinuation);
                Continuation continuation = _target.ProcessedData.GetOrAdd<Continuation>(continuationPtr);
                sb.Append(ReadAsyncStack(continuation));
            }

            nextContinuationDataPtr = nextContinuationData.Next;
        }

        string message = sb.ToString();
        return message;
    }

    string IAsync.TestFunction()
    {
        ThreadStoreData threadStoreData = _thread.GetThreadStoreData();
        TargetPointer threadPtr = threadStoreData.FirstThread;
        while (threadPtr != TargetPointer.Null)
        {
            ThreadData threadData = _thread.GetThreadData(threadPtr);
            Console.WriteLine($"Thread Id {threadData.Id} (OS Id {threadData.OSId}):");
            TargetPointer result = GetTLSNextContinuationDataAddr(threadPtr);
            if (result != TargetPointer.Null)
            {
                string asyncStacks = ReadAllAsyncStacks(result);
                Console.WriteLine(asyncStacks);
            }
            threadPtr = threadData.NextThread;
        }

        return "type not found";
    }
}
