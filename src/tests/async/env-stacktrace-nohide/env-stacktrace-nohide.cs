// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests Environment.StackTrace with the default behavior.
/// The pre-await trace should include non-async callers
/// below the async methods (the full physical call stack).
/// </summary>
public class Async2EnvStackTraceNoHide
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
        Task<(string, string)> task = OuterMethod(gate.Task);
        gate.SetResult();
        (string preAwait, string postAwait) = await task;

        Assert.True(
            preAwait.Contains(nameof(MiddleMethod), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(MiddleMethod) + "." + Environment.NewLine + preAwait);
        Assert.True(
            preAwait.Contains(nameof(OuterMethod), StringComparison.Ordinal),
            "Pre-await should contain " + nameof(OuterMethod) + "." + Environment.NewLine + preAwait);

        Assert.True(
            preAwait.Contains(nameof(SyncCaller), StringComparison.Ordinal),
            "Pre-await should preserve " + nameof(SyncCaller) + "." + Environment.NewLine + preAwait);

        // Post-await should still have continuation stitching.
        Assert.True(
            postAwait.Contains(nameof(MiddleMethod), StringComparison.Ordinal),
            "Post-await should contain " + nameof(MiddleMethod) + "." + Environment.NewLine + postAwait);
        Assert.True(
            postAwait.Contains(nameof(OuterMethod), StringComparison.Ordinal),
            "Post-await should contain " + nameof(OuterMethod) + "." + Environment.NewLine + postAwait);
        Assert.DoesNotContain(nameof(SyncCaller), postAwait);
        Console.WriteLine("=== FEATURE ENABLED: BEFORE ===" + Environment.NewLine + preAwait);
        Console.WriteLine("=== FEATURE ENABLED: AFTER ===" + Environment.NewLine + postAwait);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(string, string)> OuterMethod(Task gate)
    {
        return await MiddleMethod(gate);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(string, string)> MiddleMethod(Task gate)
    {
        string preAwait = Environment.StackTrace;
        Assert.Contains(nameof(SyncCaller), new StackTrace(StackTraceOptions.IncludeAsyncFrames).ToString());
        await gate;
        string postAwait = Environment.StackTrace;
        string stitched = new StackTrace(StackTraceOptions.IncludeAsyncFrames).ToString();
        Assert.Contains(nameof(OuterMethod), stitched);
        Assert.Contains(nameof(OuterMethod), new StackTrace(true, StackTraceOptions.IncludeAsyncFrames).ToString());
        Assert.Contains(nameof(OuterMethod), new StackTrace(0, false, StackTraceOptions.IncludeAsyncFrames).ToString());
        Assert.DoesNotContain(nameof(OuterMethod), new StackTrace().ToString());
        Assert.DoesNotContain(nameof(OuterMethod), new StackTrace(false).ToString());
        Assert.DoesNotContain(nameof(OuterMethod), new StackTrace(0).ToString());
        Assert.DoesNotContain(nameof(OuterMethod), new StackTrace(0, false).ToString());
        Assert.DoesNotContain(nameof(OuterMethod), new StackTrace(StackTraceOptions.None).ToString());
        Assert.DoesNotContain(nameof(OuterMethod), new StackTrace(false, StackTraceOptions.None).ToString());
        Assert.DoesNotContain(nameof(OuterMethod), new StackTrace(0, true, StackTraceOptions.None).ToString());
        Assert.Contains(nameof(OuterMethod), new StackTrace(StackTraceOptions.IncludeAsyncFrames).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackTrace((StackTraceOptions)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackTrace(false, (StackTraceOptions)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackTrace(-1, false, StackTraceOptions.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackTrace(0, false, (StackTraceOptions)2));
        var unskipped = new StackTrace(0, false, StackTraceOptions.IncludeAsyncFrames);
        var skipped = new StackTrace(1, false, StackTraceOptions.IncludeAsyncFrames);
        Assert.Equal(unskipped.FrameCount - 1, skipped.FrameCount);
        Assert.Contains(nameof(OuterMethod), skipped.ToString());
        Assert.Equal(0, new StackTrace(1000, false, StackTraceOptions.IncludeAsyncFrames).FrameCount);
        Console.WriteLine("=== SAME CAPTURE: EXPLICIT ASYNC ===" + Environment.NewLine + stitched);
        Console.WriteLine("=== SAME CAPTURE: LEGACY ===" + Environment.NewLine + new StackTrace());
        return (preAwait, postAwait);
    }

    [Fact]
    [RuntimeAsyncMethodGeneration(false)]
    public static void TestExceptionCapture()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task task = ThrowingOuter(gate.Task);
        gate.SetResult();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        string trace = new StackTrace(exception).ToString();
        Assert.Contains(nameof(ThrowingInner), trace);
        Assert.Contains(nameof(ThrowingOuter), trace);
        Assert.Contains(nameof(ThrowingOuter), new StackTrace(exception, true).ToString());
        Console.WriteLine("=== EXCEPTION ===" + Environment.NewLine + trace);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ThrowingOuter(Task gate) => await ThrowingInner(gate);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ThrowingInner(Task gate)
    {
        await gate;
        throw new InvalidOperationException("capture test");
    }
}
