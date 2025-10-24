// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DataContractReader.Data;

public sealed class Continuation : IData<Continuation>
{
    private const string TypeName = "Continuation";
    private const string TypeNamespace = "System.Runtime.CompilerServices";

    private static bool _parsed;
    private static Dictionary<string, uint> _fieldOffsets = [];

    static Continuation IData<Continuation>.Create(Target target, TargetPointer address) => new Continuation(target, address);

    public Continuation(Target target, TargetPointer address)
    {
        if (!_parsed)
        {
            _fieldOffsets = ManagedDataHelpers.ParseOffsets(target, TypeName, TypeNamespace);
            _parsed = true;
        }

        Address = address;
        Next = target.ReadPointer(address + _fieldOffsets[nameof(Next)] + (uint)target.PointerSize);
        Resume = target.ReadPointer(address + _fieldOffsets[nameof(Resume)] + (uint)target.PointerSize);
        State = target.Read<uint>(address + _fieldOffsets[nameof(State)] + (uint)target.PointerSize);
        Flags = target.Read<uint>(address + _fieldOffsets[nameof(Flags)] + (uint)target.PointerSize);
    }

    public TargetPointer Address { get; }

    public TargetPointer Next { get; }
    public TargetPointer Resume { get; }
    public uint State { get; }
    public uint Flags { get; }
}
