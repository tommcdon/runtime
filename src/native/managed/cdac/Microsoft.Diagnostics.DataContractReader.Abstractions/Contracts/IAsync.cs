// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.DataContractReader.Contracts;

public readonly record struct AsyncLocal(
    uint ILVarNum,
    TargetPointer Address);

public readonly record struct ResumeData(
    MethodDescHandle MethodDesc,
    TargetCodePointer CodeStart,
    uint DiagnosticsOffset,
    IEnumerable<AsyncLocal> Locals);

public interface IAsync : IContract
{
    static string IContract.Name { get; } = nameof(Async);

    IEnumerable<IEnumerable<ResumeData>> GetAsyncData(TargetPointer thread) => throw new NotImplementedException();
    ImmutableArray<TypeHandle> ParseLocal(ResumeData rd) => throw new NotImplementedException();
}

public readonly struct Async : IAsync
{
    // throws NotImplementedException for all methods
}
