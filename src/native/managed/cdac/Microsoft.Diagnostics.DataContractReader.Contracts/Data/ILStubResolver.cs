// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DataContractReader.Data;

internal sealed class ILStubResolver : IData<ILStubResolver>
{
    static ILStubResolver IData<ILStubResolver>.Create(Target target, TargetPointer address)
        => new ILStubResolver(target, address);

    public ILStubResolver(Target target, TargetPointer address)
    {
        Target.TypeInfo type = target.GetTypeInfo(DataType.ILStubResolver);

        StubTargetMD = target.ReadPointer(address + (ulong)type.Fields[nameof(StubTargetMD)].Offset);
    }

    public TargetPointer StubTargetMD { get; }
}
