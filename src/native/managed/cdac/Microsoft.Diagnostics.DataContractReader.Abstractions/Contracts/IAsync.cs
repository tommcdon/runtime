// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DataContractReader.Contracts;

public readonly record struct ResumeData(
    MethodDescHandle MethodDesc,
    TargetCodePointer CodeStart,
    uint ResumeOffset,
    uint JoinOffset,
    uint NumArgs);

public interface IAsync : IContract
{
    static string IContract.Name { get; } = nameof(Async);

    IEnumerable<IEnumerable<ResumeData>> GetAsyncData(TargetPointer thread) => throw new NotImplementedException();
}

public readonly struct Async : IAsync
{
    // throws NotImplementedException for all methods
}
