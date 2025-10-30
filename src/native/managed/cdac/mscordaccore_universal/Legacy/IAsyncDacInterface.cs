// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Microsoft.Diagnostics.DataContractReader.Legacy;

internal struct DacpAsyncFrameData
{
    public int frameId;
    public ClrDataAddress module;
    public uint funcMetadataToken;
    public ClrDataAddress methodDesc;
    public ClrDataAddress codeStartAddr;
    public ulong diagnosticOffset;
    public uint numVars;
}

internal struct DacpAsyncLocalData
{
    public ClrDataAddress address;
    public uint ilVarNum;
}

[GeneratedComInterface]
[Guid("7d46a03c-26d7-44fb-9ff3-49a699511fd7")]
internal unsafe partial interface IAsyncDacInterface
{
    [PreserveSig]
    int GetAsyncChainCount(
        ClrDataAddress thread,
        int* chains);

    [PreserveSig]
    int GetAsyncCallStack(
        ClrDataAddress thread,
        int chainId,
        int count,
        [In, Out, MarshalUsing(CountElementName = nameof(count))] DacpAsyncFrameData[]? values,
        int* pNeeded);

    [PreserveSig]
    int GetAsyncLocals(
        ClrDataAddress thread,
        int chainId,
        int frameId,
        int count,
        [In, Out, MarshalUsing(CountElementName = nameof(count))] DacpAsyncLocalData[]? values,
        int* pNeeded);
}
