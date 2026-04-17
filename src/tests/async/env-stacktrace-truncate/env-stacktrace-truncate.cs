// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests Environment.StackTrace with DOTNET_StackTraceAsyncBehavior=2 (truncate mode).
/// Mode 2 keeps non-async frames between async frames but truncates trailing
/// non-async frames below the last async frame.
/// </summary>
public class Async2EnvStackTraceTruncate
{
    [Fact]
    public static void TestEntryPoint()
    {
        SyncCaller();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [RuntimeAsyncMethodGeneration(false)]
    private static void SyncCaller()
    {
        OuterAsync().GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task OuterAsync()
    {
        string preAwait = await NonAsyncBridge();

        // NonAsyncBridge sits between OuterAsync and InnerAsync.
        // Mode 2 should KEEP it (it's between async frames).
        Assert.True(
            preAwait.Contains(nameof(NonAsyncBridge), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(NonAsyncBridge) + " (interim non-async frame)." + Environment.NewLine + preAwait);

        // Both async frames should be present.
        Assert.True(
            preAwait.Contains(nameof(InnerAsync), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(InnerAsync) + "." + Environment.NewLine + preAwait);
        Assert.True(
            preAwait.Contains(nameof(OuterAsync), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(OuterAsync) + "." + Environment.NewLine + preAwait);

        // SyncCaller is a trailing non-async frame below the last async frame.
        // Mode 2 should TRUNCATE it.
        Assert.False(
            preAwait.Contains(nameof(SyncCaller), StringComparison.Ordinal),
            "Pre-await should not contain " + nameof(SyncCaller) + " (trailing non-async frame)." + Environment.NewLine + preAwait);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [RuntimeAsyncMethodGeneration(false)]
    private static Task<string> NonAsyncBridge()
    {
        return InnerAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> InnerAsync()
    {
        string trace = Environment.StackTrace;
        await Task.Delay(1);
        return trace;
    }
}
