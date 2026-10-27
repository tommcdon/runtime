// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;

namespace Microsoft.Diagnostics.DataContractReader.Legacy;

public sealed class NativeInterfaceOwner
{
    private ComObject? _dataTarget;
    private ComObject? _legacyDac;
    private int _released;

    public NativeInterfaceOwner(ComObject? dataTarget = null, ComObject? legacyDac = null)
    {
        _dataTarget = dataTarget;
        _legacyDac = legacyDac;
    }

    public void SetLegacyDac(ComObject legacyDac)
    {
        if (Volatile.Read(ref _released) != 0)
            throw new ObjectDisposedException(nameof(NativeInterfaceOwner));

        _legacyDac = legacyDac;
    }

    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;

        Interlocked.Exchange(ref _legacyDac, null)?.FinalRelease();
        Interlocked.Exchange(ref _dataTarget, null)?.FinalRelease();
    }
}
