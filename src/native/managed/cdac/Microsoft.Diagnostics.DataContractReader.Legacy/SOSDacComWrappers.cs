// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Microsoft.Diagnostics.DataContractReader.Legacy;

internal interface IComFinalRelease
{
    void OnFinalRelease();
}

public sealed unsafe class SOSDacComWrappers : ComWrappers
{
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly delegate* unmanaged[MemberFunction]<nint, uint> s_runtimeRelease;
    private static readonly ComInterfaceEntry* s_entries;
    private static readonly int s_entryCount;

    public static SOSDacComWrappers Instance { get; } = new();

    static SOSDacComWrappers()
    {
        GetIUnknownImpl(out nint queryInterface, out nint addRef, out nint release);
        s_runtimeRelease = (delegate* unmanaged[MemberFunction]<nint, uint>)release;

        IComExposedDetails details = StrategyBasedComWrappers.DefaultIUnknownInterfaceDetailsStrategy
            .GetComExposedTypeDetails(typeof(SOSDacImpl).TypeHandle)
            ?? throw new InvalidOperationException($"No generated COM details found for {nameof(SOSDacImpl)}.");
        ComInterfaceEntry* generatedEntries = details.GetComInterfaceEntries(out int generatedCount);

        s_entryCount = generatedCount + 1;
        s_entries = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(
            typeof(SOSDacImpl),
            sizeof(ComInterfaceEntry) * s_entryCount);

        nint* unknownVtable = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(
            typeof(SOSDacImpl),
            sizeof(nint) * 3);
        unknownVtable[0] = queryInterface;
        unknownVtable[1] = addRef;
        unknownVtable[2] = (nint)(delegate* unmanaged[MemberFunction]<nint, uint>)&Release;
        s_entries[0] = new ComInterfaceEntry
        {
            IID = IID_IUnknown,
            Vtable = (nint)unknownVtable,
        };

        for (int i = 0; i < generatedCount; i++)
        {
            s_entries[i + 1] = generatedEntries[i];
            nint* vtable = (nint*)generatedEntries[i].Vtable;
            vtable[2] = (nint)(delegate* unmanaged[MemberFunction]<nint, uint>)&Release;
        }
    }

    public nint GetOrCreateInterface(SOSDacImpl implementation, in Guid interfaceId)
    {
        nint unknown = GetOrCreateUnknown(implementation);
        int hr = Marshal.QueryInterface(unknown, interfaceId, out nint result);
        Marshal.Release(unknown);
        Marshal.ThrowExceptionForHR(hr);
        return result;
    }

    public nint GetOrCreateUnknown(SOSDacImpl implementation) =>
        GetOrCreateComInterfaceForObject(
            implementation,
            CreateComInterfaceFlags.CallerDefinedIUnknown);

    protected override ComInterfaceEntry* ComputeVtables(
        object obj,
        CreateComInterfaceFlags flags,
        out int count)
    {
        if (obj is not SOSDacImpl ||
            !flags.HasFlag(CreateComInterfaceFlags.CallerDefinedIUnknown))
        {
            count = 0;
            return null;
        }

        count = s_entryCount;
        return s_entries;
    }

    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) =>
        throw new NotSupportedException();

    protected override void ReleaseObjects(IEnumerable objects) =>
        throw new NotSupportedException();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static uint Release(nint thisPtr)
    {
        object instance = ComInterfaceDispatch.GetInstance<object>((ComInterfaceDispatch*)thisPtr);
        uint count = s_runtimeRelease(thisPtr);
        if (count == 0 && instance is IComFinalRelease finalRelease)
        {
            try
            {
                finalRelease.OnFinalRelease();
            }
            catch (Exception ex)
            {
                Environment.FailFast("Failed to release cDAC native COM inputs.", ex);
            }
        }

        GC.KeepAlive(instance);
        return count;
    }
}
