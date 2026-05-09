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

    /// <summary>
    /// Validates the task waiter chain: when a sync (non-async) method sits
    /// between two v2 async methods, the inner method suspends independently
    /// from the outer method, creating separate RuntimeAsyncTasks linked
    /// through m_continuationObject. After resume, Environment.StackTrace
    /// must recover the outer caller's frames from the waiter chain.
    ///
    /// Call chain: PipelineOuterAsync (v2) -> SyncBridge (sync) -> PipelineInnerAsync (v2) -> Task.Delay
    /// </summary>
    [Fact]
    public static void TestSyncBridgeWaiterChain()
    {
        SyncBridgeEntry().GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [RuntimeAsyncMethodGeneration(false)]
    private static async Task SyncBridgeEntry()
    {
        Task<string> outerTask = PipelineOuterAsync();
        string trace = await outerTask;

        int innerIdx = trace.IndexOf(nameof(PipelineInnerAsync), StringComparison.Ordinal);
        int outerIdx = trace.IndexOf(nameof(PipelineOuterAsync), StringComparison.Ordinal);

        Assert.True(
            innerIdx >= 0,
            "Expected post-await trace to contain " + nameof(PipelineInnerAsync) + "." + Environment.NewLine + trace);

        Assert.True(
            outerIdx >= 0,
            "Expected post-await trace to contain " + nameof(PipelineOuterAsync) + " (recovered via task waiter chain)." + Environment.NewLine + trace);

        Assert.True(
            innerIdx < outerIdx,
            nameof(PipelineInnerAsync) + " should appear before " + nameof(PipelineOuterAsync) + " in the trace (inner is the active frame, outer is the waiter)." + Environment.NewLine + trace);

        Assert.False(
            trace.Contains("DispatchContinuations", StringComparison.Ordinal),
            "Expected DispatchContinuations to be hidden." + Environment.NewLine + trace);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> PipelineOuterAsync()
    {
        string result = await SyncBridge();
        return result;
    }

    /// <summary>
    /// Plain synchronous method that forwards the Task without awaiting.
    /// Because SyncBridge is not a v2 async method, the inner method's
    /// RuntimeAsyncTask is created first; then the outer v2 async caller
    /// awaits the returned Task and creates its own RuntimeAsyncTask.
    /// The two RuntimeAsyncTasks are connected through m_continuationObject,
    /// possibly via intermediate wrapper types (e.g. TaskContinuation).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<string> SyncBridge()
    {
        return PipelineInnerAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> PipelineInnerAsync()
    {
        await Task.Delay(1);
        return Environment.StackTrace;
    }

    /// <summary>
    /// Validates deeper waiter chain walking with multiple sync bridges,
    /// mimicking an ASP.NET-style middleware pipeline where sync dispatch
    /// layers sit between v2 async request processing methods.
    ///
    /// Call chain: DeepOuter (v2) -> SyncLayer1 -> DeepMiddle (v2) -> SyncLayer2 -> DeepHandler (v2) -> Task.Delay
    ///
    /// This creates three separate RuntimeAsyncTasks linked via waiter chain:
    ///   DeepHandler.RAT -> m_continuationObject -> DeepMiddle.RAT -> m_continuationObject -> DeepOuter.RAT
    /// </summary>
    [Fact]
    public static void TestDeepSyncBridgeWaiterChain()
    {
        DeepPipelineEntry().GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [RuntimeAsyncMethodGeneration(false)]
    private static async Task DeepPipelineEntry()
    {
        string trace = await DeepOuterAsync();

        int handlerIdx = trace.IndexOf(nameof(DeepHandlerAsync), StringComparison.Ordinal);
        int middleIdx = trace.IndexOf(nameof(DeepMiddleAsync), StringComparison.Ordinal);
        int outerIdx = trace.IndexOf(nameof(DeepOuterAsync), StringComparison.Ordinal);

        Assert.True(
            handlerIdx >= 0,
            "Expected trace to contain " + nameof(DeepHandlerAsync) + "." + Environment.NewLine + trace);

        Assert.True(
            middleIdx >= 0,
            "Expected trace to contain " + nameof(DeepMiddleAsync) + " (recovered from waiter chain depth 1)." + Environment.NewLine + trace);

        Assert.True(
            outerIdx >= 0,
            "Expected trace to contain " + nameof(DeepOuterAsync) + " (recovered from waiter chain depth 2)." + Environment.NewLine + trace);

        Assert.True(
            handlerIdx < middleIdx && middleIdx < outerIdx,
            "Expected waiter chain ordering: " + nameof(DeepHandlerAsync) + " < " + nameof(DeepMiddleAsync) + " < " + nameof(DeepOuterAsync) + "." + Environment.NewLine + trace);

        Assert.False(
            trace.Contains("DispatchContinuations", StringComparison.Ordinal),
            "Expected DispatchContinuations to be hidden." + Environment.NewLine + trace);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> DeepOuterAsync()
    {
        return await SyncLayer1();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<string> SyncLayer1()
    {
        return DeepMiddleAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> DeepMiddleAsync()
    {
        return await SyncLayer2();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<string> SyncLayer2()
    {
        return DeepHandlerAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> DeepHandlerAsync()
    {
        await Task.Delay(1);
        return Environment.StackTrace;
    }
}
