// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DataContractReader.Data;

public sealed class ResumeInfo : IData<ResumeInfo>
{
    private const string TypeName = "ResumeInfo";
    private const string TypeNamespace = "System.Runtime.CompilerServices";

    private static bool _parsed;
    private static Dictionary<string, uint> _fieldOffsets = [];

    static ResumeInfo IData<ResumeInfo>.Create(Target target, TargetPointer address) => new ResumeInfo(target, address);

    public ResumeInfo(Target target, TargetPointer address)
    {
        if (!_parsed)
        {
            _fieldOffsets = ManagedDataHelpers.ParseOffsets(target, TypeName, TypeNamespace);
            _parsed = true;
        }

        Resume = target.ReadCodePointer(address + _fieldOffsets[nameof(Resume)]);
        DiagnosticIP = target.ReadCodePointer(address + _fieldOffsets[nameof(DiagnosticIP)]);
    }


    public TargetCodePointer Resume { get; }
    public TargetCodePointer DiagnosticIP { get; }
}
