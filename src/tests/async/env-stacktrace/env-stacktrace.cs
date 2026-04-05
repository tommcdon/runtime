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
        string stackTrace = await OuterMethod();

        // MiddleMethod captures Environment.StackTrace after InnerMethod completes
        // and MiddleMethod resumes via continuation dispatch.
        Assert.True(
            stackTrace.Contains(nameof(MiddleMethod), StringComparison.Ordinal),
            "Expected Environment.StackTrace to contain " + nameof(MiddleMethod) + "." + Environment.NewLine + stackTrace);

        // OuterMethod is NOT on the physical stack (it's a suspended caller),
        // but async v2 continuation tracking should inject it into the trace.
        Assert.True(
            stackTrace.Contains(nameof(OuterMethod), StringComparison.Ordinal),
            "Expected Environment.StackTrace to contain " + nameof(OuterMethod) + "." + Environment.NewLine + stackTrace);

        // The internal dispatch frame (DispatchContinuations) should be
        // filtered out of the visible stack trace.
        Assert.False(
            stackTrace.Contains("DispatchContinuations", StringComparison.Ordinal),
            "Expected Environment.StackTrace not to contain DispatchContinuations." + Environment.NewLine + stackTrace);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> OuterMethod()
    {
        return await MiddleMethod();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> MiddleMethod()
    {
        await InnerMethod();

        // After InnerMethod completes, MiddleMethod resumes via
        // DispatchContinuations, which sets up the continuation chain.
        return Environment.StackTrace;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task InnerMethod()
    {
        await Task.Delay(1);
    }
}
