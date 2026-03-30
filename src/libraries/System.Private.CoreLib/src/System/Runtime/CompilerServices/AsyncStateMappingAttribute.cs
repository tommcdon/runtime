// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Emitted by the compiler on runtime-async methods that have been edited
    /// (via Edit and Continue or Hot Reload). Contains the mapping from
    /// previous-generation state IDs to current-generation state IDs, enabling
    /// the JIT to build a resume dispatch table that handles in-flight
    /// continuations suspended under the old version.
    /// </summary>
    /// <remarks>
    /// The mapping array is a flat sequence of (oldState, newState) pairs.
    /// A newState of -1 indicates a deleted await point — the JIT should emit a
    /// dispatch entry that throws <see cref="InvalidOperationException"/> for that state,
    /// matching the async v1 behavior of GenerateMissingStateDispatch.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    [CLSCompliant(false)]
    public sealed class AsyncStateMappingAttribute : Attribute
    {
        public int[] Mapping { get; }

        public AsyncStateMappingAttribute(int[] mapping)
        {
            Mapping = mapping;
        }
    }
}
