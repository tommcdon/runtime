// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.DataContractReader.Contracts;

public record AsyncLocal(
    uint ILVarNum,
    TargetPointer Address,
    TypeHandle? Type);

public record ResumeData(
    TargetCodePointer ResumePoint,
    TargetCodePointer DiagnosticIP);

public interface IAsync : IContract
{
    static string IContract.Name { get; } = nameof(Async);

    IEnumerable<IEnumerable<ResumeData>> GetAsyncData(TargetPointer thread) => throw new NotImplementedException();
    IEnumerable<AsyncLocal> GetLocals(ResumeData rd) => throw new NotImplementedException();
}

public readonly struct Async : IAsync
{
    // throws NotImplementedException for all methods
}
