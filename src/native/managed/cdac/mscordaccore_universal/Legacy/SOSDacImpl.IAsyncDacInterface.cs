// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Microsoft.Diagnostics.DataContractReader.Contracts;

namespace Microsoft.Diagnostics.DataContractReader.Legacy;

/// <summary>
/// Implementation of IAsyncDacInterface interface intended to be passed out to consumers
/// interacting with the DAC via those COM interfaces.
/// </summary>
internal sealed unsafe partial class SOSDacImpl : IAsyncDacInterface
{
    int IAsyncDacInterface.GetAsyncChainCount(ClrDataAddress thread, int* chains)
    {
        int hr = HResults.S_OK;

        try
        {
            if (thread == 0)
                throw new ArgumentException("Thread address is null", nameof(thread));

            if (chains is null)
                throw new ArgumentNullException(nameof(chains));

            IAsync async = _target.Contracts.Async;
            IEnumerable<IEnumerable<ResumeData>> threadResumeDatas = async.GetAsyncData(thread.ToTargetPointer(_target));
            *chains = threadResumeDatas.Count();
        }
        catch (System.Exception ex)
        {
            hr = ex.HResult;
        }

        return hr;
    }

    int IAsyncDacInterface.GetAsyncCallStack(
        ClrDataAddress thread,
        int chainId,
        int count,
        [In, MarshalUsing(CountElementName = nameof(count)), Out] DacpAsyncFrameData[]? values,
        int* pNeeded)
    {
        int hr = HResults.S_OK;

        try
        {
            if (thread == 0)
                throw new ArgumentException("Thread address is null", nameof(thread));

            IThread contract = _target.Contracts.Thread;
            IAsync async = _target.Contracts.Async;
            IRuntimeTypeSystem rts = _target.Contracts.RuntimeTypeSystem;
            IExecutionManager eman = _target.Contracts.ExecutionManager;

            IEnumerable<IEnumerable<ResumeData>> threadResumeDatas = async.GetAsyncData(thread.ToTargetPointer(_target));
            if (threadResumeDatas.Skip(chainId).FirstOrDefault() is not IEnumerable<ResumeData> currentChainEnumerable)
                throw new ArgumentException("Invalid chainId", nameof(chainId));

            List<ResumeData> currentChain = currentChainEnumerable.ToList();

            if (values is not null)
            {
                int index = 0;
                foreach (ResumeData resumeData in currentChain)
                {
                    if (index >= count)
                        break;

                    if (eman.GetCodeBlockHandle(resumeData.DiagnosticIP) is not CodeBlockHandle cbh)
                        throw Marshal.GetExceptionForHR(HResults.E_FAIL)!;

                    TargetPointer methodDescPtr = eman.GetMethodDesc(cbh);
                    MethodDescHandle mdHandle = rts.GetMethodDescHandle(methodDescPtr);
                    uint mdToken = rts.GetMethodToken(mdHandle);
                    TypeHandle typeHandle = rts.GetTypeHandle(rts.GetMethodTable(mdHandle));
                    TargetPointer modulePtr = rts.GetModule(typeHandle);
                    TargetCodePointer codeStartPtr = eman.GetStartAddress(cbh);

                    values[index].module = modulePtr.ToClrDataAddress(_target);
                    values[index].funcMetadataToken = mdToken;
                    values[index].methodDesc = methodDescPtr.ToClrDataAddress(_target);
                    values[index].codeStartAddr = codeStartPtr.ToClrDataAddress(_target);
                    values[index].diagnosticOffset = resumeData.DiagnosticIP.ToClrDataAddress(_target);
                    values[index].numVars = (uint)async.GetLocals(resumeData).Count();
                    index++;
                }
            }

            if (pNeeded is not null)
                *pNeeded = currentChain.Count;
        }
        catch (System.Exception ex)
        {
            hr = ex.HResult;
        }

        return hr;
    }

    int IAsyncDacInterface.GetAsyncLocals(
        ClrDataAddress thread,
        int chainId,
        int frameId,
        int count,
        [In, MarshalUsing(CountElementName = nameof(count)), Out] DacpAsyncLocalData[]? values,
        int* pNeeded)
    {
        int hr = HResults.S_OK;

        try
        {
            if (thread == 0)
                throw new ArgumentException("Thread address is null", nameof(thread));

            IThread contract = _target.Contracts.Thread;
            IAsync async = _target.Contracts.Async;
            IRuntimeTypeSystem rts = _target.Contracts.RuntimeTypeSystem;
            IExecutionManager eman = _target.Contracts.ExecutionManager;

            IEnumerable<IEnumerable<ResumeData>> threadResumeDatas = async.GetAsyncData(thread.ToTargetPointer(_target));
            if (threadResumeDatas.Skip(chainId).FirstOrDefault() is not IEnumerable<ResumeData> currentChain)
                throw new ArgumentException("Invalid chainId", nameof(chainId));
            if (currentChain.Skip(frameId).FirstOrDefault() is not ResumeData rd)
                throw new ArgumentException("Invalid frameId", nameof(frameId));

            List<AsyncLocal> locals = async.GetLocals(rd).ToList();

            if (values is not null)
            {
                int index = 0;
                foreach (AsyncLocal local in locals)
                {
                    if (index >= count)
                        break;

                    values[index].address = local.Address.ToClrDataAddress(_target);
                    values[index].ilVarNum = local.ILVarNum;
                    index++;
                }
            }

            if (pNeeded is not null)
                *pNeeded = locals.Count;
        }
        catch (System.Exception ex)
        {
            hr = ex.HResult;
        }

        return hr;

    }
}
