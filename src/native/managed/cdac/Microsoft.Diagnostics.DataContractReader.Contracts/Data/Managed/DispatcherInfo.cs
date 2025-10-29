// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DataContractReader.Data;

public sealed class DispatcherInfo : IData<DispatcherInfo>
{
    private const string TypeName = "AsyncHelpers+RuntimeAsyncTaskCore+DispatcherInfo";
    private const string TypeNamespace = "System.Runtime.CompilerServices";

    private static bool _parsed;
    private static Dictionary<string, uint> _fieldOffsets = [];

    static DispatcherInfo IData<DispatcherInfo>.Create(Target target, TargetPointer address) => new DispatcherInfo(target, address);

    public DispatcherInfo(Target target, TargetPointer address)
    {
        if (!_parsed)
        {
            _fieldOffsets = ManagedDataHelpers.ParseOffsets(target, TypeName, TypeNamespace);
            _parsed = true;
        }

        Next = target.ReadPointer(address + _fieldOffsets[nameof(Next)]);
        NextContinuation = target.ReadPointer(address + _fieldOffsets[nameof(NextContinuation)]);
    }

    // Pointer to another DispatcherInfo object
    public TargetPointer Next { get; }

    // Pointer to a Continuation object
    public TargetPointer NextContinuation { get; }
}
