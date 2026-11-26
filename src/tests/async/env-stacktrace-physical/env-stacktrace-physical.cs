// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests the Environment.StackTrace compatibility switch, independently of explicit capture options.
/// </summary>
public class Async2EnvStackTracePhysical
{
    [Fact]
    public static void TestEntryPoint()
    {
        SyncCaller();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Runtime.CompilerServices.RuntimeAsyncMethodGeneration(false)]
    private static void SyncCaller()
    {
        AsyncEntry().GetAwaiter().GetResult();
    }

    [System.Runtime.CompilerServices.RuntimeAsyncMethodGeneration(false)]
    private static async Task AsyncEntry()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(string, string, string, string)> task = OuterMethod(gate.Task);
        gate.SetResult();
        (string preAwait, string postAwait, string explicitAsync, string explicitPhysical) = await task;

        // Pre-await: physical stack is intact, so all methods appear.
        Assert.True(
            preAwait.Contains(nameof(MiddleMethod), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(MiddleMethod) + "." + Environment.NewLine + preAwait);
        Assert.True(
            preAwait.Contains(nameof(OuterMethod), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(OuterMethod) + "." + Environment.NewLine + preAwait);
        Assert.True(
            preAwait.Contains(nameof(SyncCaller), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(SyncCaller) + "." + Environment.NewLine + preAwait);

        // Post-await: no continuation stitching, so OuterMethod is NOT on the
        // physical stack (it was a suspended caller). MiddleMethod is the resumed
        // method so it IS on the physical stack.
        Assert.True(
            postAwait.Contains(nameof(MiddleMethod), StringComparison.Ordinal),
            "Post-await should contain " + nameof(MiddleMethod) + "." + Environment.NewLine + postAwait);
        Assert.False(
            postAwait.Contains(nameof(OuterMethod), StringComparison.Ordinal),
            "Post-await should NOT contain " + nameof(OuterMethod) + " with physical-only mode." + Environment.NewLine + postAwait);

        // SyncCaller is also not on the physical stack post-await.
        Assert.False(
            postAwait.Contains(nameof(SyncCaller), StringComparison.Ordinal),
            "Post-await should NOT contain " + nameof(SyncCaller) + " with physical-only mode." + Environment.NewLine + postAwait);

        Assert.Contains(nameof(OuterMethod), explicitAsync);
        Assert.DoesNotContain(nameof(OuterMethod), explicitPhysical);
        Console.WriteLine("=== FEATURE DISABLED: ENVIRONMENT ===" + Environment.NewLine + postAwait);
        Console.WriteLine("=== FEATURE DISABLED: EXPLICIT ASYNC ===" + Environment.NewLine + explicitAsync);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(string, string, string, string)> OuterMethod(Task gate)
    {
        return await MiddleMethod(gate);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(string, string, string, string)> MiddleMethod(Task gate)
    {
        string preAwait = Environment.StackTrace;
        await gate;
        string postAwait = Environment.StackTrace;
        string explicitAsync = new StackTrace(StackTraceOptions.IncludeAsyncFrames).ToString();
        string explicitPhysical = new StackTrace(StackTraceOptions.None).ToString();
        return (preAwait, postAwait, explicitAsync, explicitPhysical);
    }
}
