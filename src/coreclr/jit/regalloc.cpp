// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "jitpch.h"
#ifdef _MSC_VER
#pragma hdrstop
#endif
#include "regalloc.h"

#if DOUBLE_ALIGN
DWORD Compiler::getCanDoubleAlign()
{
#ifdef DEBUG
    if (compStressCompile(STRESS_DBL_ALN, 20))
        return MUST_DOUBLE_ALIGN;

    return JitConfig.JitDoubleAlign();
#else
    return DEFAULT_DOUBLE_ALIGN;
#endif
}

//------------------------------------------------------------------------
// shouldDoubleAlign: Determine whether to double-align the frame
//
// Arguments:
//    refCntStk       - sum of     ref counts for all stack based variables
//    refCntEBP       - sum of     ref counts for EBP enregistered variables
//    refCntWtdEBP    - sum of wtd ref counts for EBP enregistered variables
//    refCntStkParam  - sum of     ref counts for all stack based parameters
//    refCntWtdStkDbl - sum of wtd ref counts for stack based doubles (including structs
//                      with double fields).
//
// Return Value:
//    Returns true if this method estimates that a double-aligned frame would be beneficial
//
// Notes:
//    The impact of a double-aligned frame is computed as follows:
//    - We save a byte of code for each parameter reference (they are frame-pointer relative)
//    - We pay a byte of code for each non-parameter stack reference.
//    - We save the misalignment penalty and possible cache-line crossing penalty.
//      This is estimated as 0 for SMALL_CODE, 16 for FAST_CODE and 4 otherwise.
//    - We pay 7 extra bytes for:
//        MOV EBP,ESP,
//        LEA ESP,[EBP-offset]
//        AND ESP,-8 to double align ESP
//    - We pay one extra memory reference for each variable that could have been enregistered in EBP (refCntWtdEBP).
//
//    If the misalignment penalty is estimated to be less than the bytes used, we don't double align.
//    Otherwise, we compare the weighted ref count of ebp-enregistered variables against double the
//    ref count for double-aligned values.
//
bool Compiler::shouldDoubleAlign(
    unsigned refCntStk, unsigned refCntEBP, weight_t refCntWtdEBP, unsigned refCntStkParam, weight_t refCntWtdStkDbl)
{
    bool           doDoubleAlign        = false;
    const unsigned DBL_ALIGN_SETUP_SIZE = 7;

    unsigned bytesUsed         = refCntStk + refCntEBP - refCntStkParam + DBL_ALIGN_SETUP_SIZE;
    unsigned misaligned_weight = 4;

    if (compCodeOpt() == Compiler::SMALL_CODE)
        misaligned_weight = 0;

    if (compCodeOpt() == Compiler::FAST_CODE)
        misaligned_weight *= 4;

    JITDUMP("\nDouble alignment:\n");
    JITDUMP("  Bytes that could be saved by not using EBP frame: %i\n", bytesUsed);
    JITDUMP("  Sum of weighted ref counts for EBP enregistered variables: %f\n", refCntWtdEBP);
    JITDUMP("  Sum of weighted ref counts for weighted stack based doubles: %f\n", refCntWtdStkDbl);

    if (((weight_t)bytesUsed) > ((refCntWtdStkDbl * misaligned_weight) / BB_UNITY_WEIGHT))
    {
        JITDUMP("    Predicting not to double-align ESP to save %d bytes of code.\n", bytesUsed);
    }
    else if (refCntWtdEBP > refCntWtdStkDbl * 2)
    {
        // TODO-CQ: On P4 2 Proc XEON's, SciMark.FFT degrades if SciMark.FFT.transform_internal is
        // not double aligned.
        // Here are the numbers that make this not double-aligned.
        //     refCntWtdStkDbl = 0x164
        //     refCntWtdEBP    = 0x1a4
        // We think we do need to change the heuristic to be in favor of double-align.

        JITDUMP("    Predicting not to double-align ESP to allow EBP to be used to enregister variables.\n");
    }
    else
    {
        // OK we passed all of the benefit tests, so we'll predict a double aligned frame.
        JITDUMP("    Predicting to create a double-aligned frame\n");
        doDoubleAlign = true;
    }
    return doDoubleAlign;
}
#endif // DOUBLE_ALIGN

//------------------------------------------------------------------------
// rpMustCreateEBPFrame:
//   Returns true when we must create an EBP frame
//
// Parameters:
//   wbReason - [out] Detailed reason why a frame must be created. Only valid if
//                    the function returns true.
//
// Remarks:
//   This is used to force most managed methods to have EBP based frames
//   which allows the ETW kernel stackwalker to walk the stacks of managed code
//   this allows the kernel to perform light weight profiling
//
bool Compiler::rpMustCreateEBPFrame(INDEBUG(const char** wbReason))
{
    bool result = false;
#ifdef DEBUG
    const char* reason = nullptr;
#endif

#if ETW_EBP_FRAMED
    if (!result && opts.OptimizationDisabled())
    {
        INDEBUG(reason = "Debug Code");
        result = true;
    }
    if (!result && (info.compMethodInfo->ILCodeSize > DEFAULT_MAX_INLINE_SIZE))
    {
        INDEBUG(reason = "IL Code Size");
        result = true;
    }
    if (!result && (fgBBcount > 3))
    {
        INDEBUG(reason = "BasicBlock Count");
        result = true;
    }
    if (!result && fgHasLoops)
    {
        INDEBUG(reason = "Method has Loops");
        result = true;
    }
    if (!result && (optCallCount >= optFastTailCallCount + 2))
    {
        INDEBUG(reason = "Call Count");
        result = true;
    }
    if (!result && (optIndirectCallCount >= optIndirectFastTailCallCount + 1))
    {
        INDEBUG(reason = "Indirect Call");
        result = true;
    }
#endif // ETW_EBP_FRAMED

    // VM wants to identify the containing frame of an InlinedCallFrame always
    // via the frame register never the stack register so we need a frame.
    if (!result && (optNativeCallCount != 0))
    {
        INDEBUG(reason = "Uses PInvoke");
        result = true;
    }

#ifdef TARGET_ARM64
    // TODO-ARM64-NYI: This is temporary: force a frame pointer-based frame until genFnProlog can handle non-frame
    // pointer frames.
    if (!result)
    {
        INDEBUG(reason = "Temporary ARM64 force frame pointer");
        result = true;
    }
#endif // TARGET_ARM64

#ifdef TARGET_LOONGARCH64
    // TODO-LOONGARCH64-NYI: This is temporary: force a frame pointer-based frame until genFnProlog
    // can handle non-frame pointer frames.
    if (!result)
    {
        INDEBUG(reason = "Temporary LOONGARCH64 force frame pointer");
        result = true;
    }
#endif // TARGET_LOONGARCH64

#ifdef TARGET_RISCV64
    // TODO-RISCV64-NYI: This is temporary: force a frame pointer-based frame until genFnProlog
    // can handle non-frame pointer frames.
    if (!result)
    {
        INDEBUG(reason = "Temporary RISCV64 force frame pointer");
        result = true;
    }
#endif // TARGET_RISCV64

#ifdef DEBUG
    if ((result == true) && (wbReason != nullptr))
    {
        *wbReason = reason;
    }
#endif

    return result;
}

//------------------------------------------------------------------------
// raMarkStkVars:
//   Mark all variables as to whether they live on the stack frame
//  (part or whole), and if so what the base is (FP or SP).
//
void Compiler::raMarkStkVars()
{
    unsigned   lclNum;
    LclVarDsc* varDsc;

    for (lclNum = 0, varDsc = lvaTable; lclNum < lvaCount; lclNum++, varDsc++)
    {
        // lvOnFrame is set by LSRA, except in the case of zero-ref, which is set below.

        if (lvaIsFieldOfDependentlyPromotedStruct(varDsc))
        {
            noway_assert(!varDsc->lvRegister);
            goto ON_STK;
        }

        // Fully enregistered variables don't need any frame space

        if (varDsc->lvRegister)
        {
            goto NOT_STK;
        }
        // Unused variables typically don't get any frame space
        else if (varDsc->lvRefCnt() == 0)
        {
#ifdef DEBUG
            // For debugging, note that we have to reserve space even for
            // unused variables if they are ever in scope. However, this is not
            // an issue as fgExtendDbgLifetimes() adds an initialization and
            // variables in scope will not have a zero ref-cnt.
            if (opts.compDbgCode && !varDsc->lvIsParam && varDsc->lvTracked)
            {
                for (unsigned scopeNum = 0; scopeNum < info.compVarScopesCount; scopeNum++)
                {
                    noway_assert(info.compVarScopes[scopeNum].vsdVarNum != lclNum);
                }
            }
#endif

            varDsc->lvOnFrame = false;
            // Clear the lvMustInit flag in case it is set
            varDsc->lvMustInit = false;

            goto NOT_STK;
        }

        if (!varDsc->lvOnFrame)
        {
            goto NOT_STK;
        }

    ON_STK:
        // The variable (or part of it) lives on the stack frame

        noway_assert((varDsc->lvType != TYP_UNDEF) && (varDsc->lvType != TYP_VOID) && (varDsc->lvType != TYP_UNKNOWN));
#if FEATURE_FIXED_OUT_ARGS
        noway_assert((lclNum == lvaOutgoingArgSpaceVar) || (lvaLclStackHomeSize(lclNum) != 0));
#else  // FEATURE_FIXED_OUT_ARGS
        noway_assert(lvaLclStackHomeSize(lclNum) != 0);
#endif // FEATURE_FIXED_OUT_ARGS

        varDsc->lvOnFrame = true; // Our prediction is that the final home for this local variable will be in the
                                  // stack frame

    NOT_STK:;
        varDsc->lvFramePointerBased = codeGen->isFramePointerUsed();

#if DOUBLE_ALIGN

        if (codeGen->doDoubleAlign())
        {
            noway_assert(codeGen->isFramePointerUsed() == false);

            /* All arguments are off of EBP with double-aligned frames */

            if (varDsc->lvIsParam && !varDsc->lvIsRegArg)
            {
                varDsc->lvFramePointerBased = true;
            }
        }

#endif

        // Some basic checks

        // It must be in a register, on frame, or have zero references.

        noway_assert(varDsc->lvIsInReg() || varDsc->lvOnFrame || varDsc->lvRefCnt() == 0);

        // We can't have both lvRegister and lvOnFrame
        noway_assert(!varDsc->lvRegister || !varDsc->lvOnFrame);

#ifdef DEBUG

        // For varargs functions, there should be no direct references to
        // parameter variables except for 'this' (because these were morphed
        // in the importer) and the 'arglist' parameter (which is not a GC
        // pointer). and the return buffer argument (if we are returning a
        // struct).
        // This is important because we don't want to try to report them
        // to the GC, as the frame offsets in these local variables would
        // not be correct.

        if (varDsc->lvIsParam && lvaIsArgAccessedViaVarArgsCookie(lclNum))
        {
            if (!varDsc->lvPromoted && !varDsc->lvIsStructField)
            {
                noway_assert(varDsc->lvRefCnt() == 0 && !varDsc->lvRegister && !varDsc->lvOnFrame);
            }
        }
#endif
    }
}
