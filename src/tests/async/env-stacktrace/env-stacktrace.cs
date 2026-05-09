// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

public class Async2EnvStackTrace
{
    [Fact]
    public static void TestEntryPoint()
    {
        AsyncEntry().GetAwaiter().GetResult();
    }

    [System.Runtime.CompilerServices.RuntimeAsyncMethodGeneration(false)]
    private static async Task AsyncEntry()
    {
        (string preAwait, string postAwait) = await OuterMethod();

        // With hiding ON (default), pre-await and post-await traces should
        // both contain only the runtime async method chain.
        Assert.True(
            preAwait.Contains(nameof(MiddleMethod), StringComparison.Ordinal),
            "Expected pre-await trace to contain " + nameof(MiddleMethod) + "." + Environment.NewLine + preAwait);
        Assert.True(
            preAwait.Contains(nameof(OuterMethod), StringComparison.Ordinal),
            "Expected pre-await trace to contain " + nameof(OuterMethod) + "." + Environment.NewLine + preAwait);

        // MiddleMethod captures Environment.StackTrace after InnerMethod completes
        // and MiddleMethod resumes via continuation dispatch.
        Assert.True(
            postAwait.Contains(nameof(MiddleMethod), StringComparison.Ordinal),
            "Expected post-await trace to contain " + nameof(MiddleMethod) + "." + Environment.NewLine + postAwait);

        // OuterMethod is NOT on the physical stack (it's a suspended caller),
        // but runtime async continuation tracking should inject it into the trace.
        Assert.True(
            postAwait.Contains(nameof(OuterMethod), StringComparison.Ordinal),
            "Expected post-await trace to contain " + nameof(OuterMethod) + "." + Environment.NewLine + postAwait);

        // The internal dispatch frame (DispatchContinuations) should be
        // filtered out of the visible stack trace.
        Assert.False(
            postAwait.Contains("DispatchContinuations", StringComparison.Ordinal),
            "Expected Environment.StackTrace not to contain DispatchContinuations." + Environment.NewLine + postAwait);

        // Non-async callers (e.g. AsyncEntry, TestEntryPoint) should be hidden
        // from the pre-await trace, making it consistent with the post-await trace.
        Assert.False(
            preAwait.Contains(nameof(AsyncEntry), StringComparison.Ordinal),
            "Expected pre-await trace not to contain " + nameof(AsyncEntry) + " when hiding is enabled." + Environment.NewLine + preAwait);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(string, string)> OuterMethod()
    {
        return await MiddleMethod();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(string, string)> MiddleMethod()
    {
        // Capture BEFORE the blocking await (physical call stack is intact)
        string preAwait = Environment.StackTrace;

        await InnerMethod();

        // Capture AFTER the blocking await (resumed via DispatchContinuations)
        string postAwait = Environment.StackTrace;

        return (preAwait, postAwait);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task InnerMethod()
    {
        await Task.Delay(1);
    }

    /// <summary>
    /// Validates that DispatchContinuations is hidden and continuations are stitched
    /// even when Environment.StackTrace is called from a non-async method invoked
    /// by a resumed async method (no async frames above DispatchContinuations on
    /// the physical stack between the non-async caller and the boundary).
    /// </summary>
    [Fact]
    public static void TestNonAsyncCallerAfterResume()
    {
        NonAsyncCallerEntry().GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [RuntimeAsyncMethodGeneration(false)]
    private static async Task NonAsyncCallerEntry()
    {
        string trace = await NonAsyncCallerOuterAsync();

        Assert.False(
            trace.Contains("DispatchContinuations", StringComparison.Ordinal),
            "DispatchContinuations should be hidden when called from a non-async method after resume." + Environment.NewLine + trace);

        Assert.True(
            trace.Contains(nameof(NonAsyncCallerOuterAsync), StringComparison.Ordinal),
            "Continuation stitching should inject " + nameof(NonAsyncCallerOuterAsync) + " as a continuation caller." + Environment.NewLine + trace);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> NonAsyncCallerOuterAsync()
    {
        return await NonAsyncCallerInnerAsync();
    }

    /// <summary>
    /// After resuming from await, calls a plain non-async method that captures
    /// Environment.StackTrace. This means the physical stack has:
    ///   CaptureStackFromNonAsync → NonAsyncCallerInnerAsync → DispatchContinuations → ...
    /// with no async frames above DispatchContinuations boundary except
    /// NonAsyncCallerInnerAsync itself.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> NonAsyncCallerInnerAsync()
    {
        await Task.Delay(1);
        return CaptureStackFromNonAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [RuntimeAsyncMethodGeneration(false)]
    private static string CaptureStackFromNonAsync()
    {
        return Environment.StackTrace;
    }
}
