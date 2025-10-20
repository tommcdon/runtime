// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DataContractReader.Contracts;

public interface IAsync : IContract
{
    static string IContract.Name { get; } = nameof(Async);
}

public readonly struct Async : IAsync
{
    // throws NotImplementedException for all methods
}
