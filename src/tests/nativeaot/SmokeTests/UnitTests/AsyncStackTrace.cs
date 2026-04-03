// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

class AsyncStackTrace
{
    internal static int Run()
    {
        TestAsyncContinuationStitching();
        return 100;
    }

    static void TestAsyncContinuationStitching()
    {
        AsyncEntry().GetAwaiter().GetResult();
    }

    static async Task AsyncEntry()
    {
        string stackTrace = await OuterMethod();

        Console.WriteLine("=== NativeAOT Environment.StackTrace after continuation dispatch ===");
        Console.WriteLine(stackTrace);
        Console.WriteLine("=== End StackTrace ===");

        // Verify MiddleMethod appears (it's on the physical stack or stitched)
        if (!stackTrace.Contains(nameof(MiddleMethod)))
            throw new Exception($"Stack trace missing '{nameof(MiddleMethod)}'");

        // OuterMethod should appear via async continuation stitching
        if (!stackTrace.Contains(nameof(OuterMethod)))
            throw new Exception($"Stack trace missing '{nameof(OuterMethod)}'");

        // The internal dispatch frame (DispatchContinuations) should be
        // filtered out of the visible stack trace.
        if (stackTrace.Contains("DispatchContinuations"))
            throw new Exception("Stack trace should not contain 'DispatchContinuations'");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<string> OuterMethod()
    {
        return await MiddleMethod();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<string> MiddleMethod()
    {
        await InnerMethod();
        return Environment.StackTrace;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task InnerMethod()
    {
        // Use Task.Delay instead of Task.Yield - Task.Delay returns a Task
        // which matches the v2 MatchTaskAwaitPattern, and the 1ms delay
        // forces actual asynchronous completion via timer callback.
        await Task.Delay(1);
    }
}
