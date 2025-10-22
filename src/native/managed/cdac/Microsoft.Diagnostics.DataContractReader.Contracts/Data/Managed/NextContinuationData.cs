// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DataContractReader.Data;

public sealed class NextContinuationData : IData<NextContinuationData>
{
    private const string TypeName = "AsyncHelpers+RuntimeAsyncTaskCore+NextContinuationData";
    private const string TypeNamespace = "System.Runtime.CompilerServices";

    private static bool _parsed;
    private static Dictionary<string, uint> _fieldOffsets = [];

    static NextContinuationData IData<NextContinuationData>.Create(Target target, TargetPointer address) => new NextContinuationData(target, address);

    public NextContinuationData(Target target, TargetPointer address)
    {
        if (!_parsed)
        {
            _fieldOffsets = ManagedDataHelpers.ParseOffsets(target, TypeName, TypeNamespace);
            _parsed = true;
        }

        Next = target.ReadPointer(address + _fieldOffsets[nameof(Next)]);
        NextContinuation = target.ReadPointer(address + _fieldOffsets[nameof(NextContinuation)]);
    }

    // Pointer to another NextContinuationData object
    public TargetPointer Next { get; }

    // Pointer to a pointer to a Continuation object
    public TargetPointer NextContinuation { get; }
}
