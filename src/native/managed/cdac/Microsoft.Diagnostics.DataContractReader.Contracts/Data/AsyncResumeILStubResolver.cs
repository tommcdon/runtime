// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DataContractReader.Data;

internal sealed class AsyncResumeILStubResolver : IData<AsyncResumeILStubResolver>
{
    static AsyncResumeILStubResolver IData<AsyncResumeILStubResolver>.Create(Target target, TargetPointer address)
        => new AsyncResumeILStubResolver(target, address);

    public AsyncResumeILStubResolver(Target target, TargetPointer address)
    {
        Target.TypeInfo type = target.GetTypeInfo(DataType.AsyncResumeILStubResolver);

        FinalResumeIP = target.ReadPointer(address + (ulong)type.Fields[nameof(FinalResumeIP)].Offset);
    }

    public TargetPointer FinalResumeIP { get; }
}
