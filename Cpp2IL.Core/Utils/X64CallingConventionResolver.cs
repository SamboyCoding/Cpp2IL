using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.PE;

namespace Cpp2IL.Core.Utils;

#pragma warning disable IDE0305, IDE0300

public static class X64CallingConventionResolver
{
    // TODO: GCC(Linux) ABI

    // This class must be used in good faith.
    // If that's not possible, uncomment the binary type checks.
    // This *will* break everything on x32.

    const int ptrSize = 8;

    private static bool IsXMM(ParameterAnalysisContext par) => par.ParameterType.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8;

    public static InstructionSetIndependentOperand[] ResolveForUnmanaged(ApplicationAnalysisContext app, ulong target)
    {
        // This is mostly a stub and may be extended in the future. You can traverse exports here for example.
        // For now, we'll return all normal registers and omit the floating point registers.

        return app.Binary is PE ? new[] {
            ToOperand(MicrosoftNormalRegister.rcx),
            ToOperand(MicrosoftNormalRegister.rdx),
            ToOperand(MicrosoftNormalRegister.r8),
            ToOperand(MicrosoftNormalRegister.r9)
        } : new[] {
            ToOperand(LinuxNormalRegister.rdi),
            ToOperand(LinuxNormalRegister.rsi),
            ToOperand(LinuxNormalRegister.rdx),
            ToOperand(LinuxNormalRegister.rcx),
            ToOperand(LinuxNormalRegister.r8),
            ToOperand(LinuxNormalRegister.r9)
        };
    }

    public static InstructionSetIndependentOperand[] ResolveForManaged(MethodAnalysisContext ctx)
    {
        // if (ctx.AppContext.Binary.is32Bit)
        //    throw new NotSupportedException("Resolution of 64-bit calling conventions in 32-bit binaries is not supported.");

        List<InstructionSetIndependentOperand> args = new();

        var addThis = !ctx.IsStatic;
        var isReturningAnOversizedStructure = false; // TODO: Determine whether we return a structure and whether that structure is oversized.

        /*
        GCC:
        Small structures - packed into N registers:
        {
            int x;
            int y;
        }
        will be packed as a single normal register

        Big structures - packed into N registers:
        {
            int x;
            int y;
            int z;
            int w;
        }
        will be packed as two normal registers

        Large structures - always in the stack:
        {
            int x;
            int y;
            int z;
            int w;
            int kk;
            int mm;
        }
        will be packed into the stack

        Small XMM structures - packed into NX registers:
        {
            int x;
            double y;
        }
        x will be packed into normal register, y will be packed into fp register, they do overlap (xmm0 and rdi are used)

        Fit XMM structures - packed into X registers:
        {
            double x;
            double y;
        }
        will be packed as 2 xmm registers

        Big XMM structures - always in the stack:
        {
            double x;
            double y;
            int z;
        }
        will be packed into the stack, even though technically you could pack it into 1 normal and 2 xmm registers (same goes for if the int is a double)

        Small float structures - packed into N registers:
        {
            float x;
            int y;
        }
        x and y will be packed into a single normal register

        Fit float structures - packed into XN registers:
        {
            float x;
            float y;
            int z;
        }
        x and y will be packed into a single fp register(doesn't match int behavior!!!), z will be packed into a normal register

        Complete float structures - packed into X registers:
        {
            float x;
            float y;
            float z;
            float w;
        }
        x,y and z,w will be packed into 2 fp registers

        Everything else is always in the stack.
        Multiple structures in args also follow this rule.
        16-byte structures will be put into the stack after the 8th structure. The others stay in registers according to spec.
        32-byte structures will be put into the stack after the 4th structure. The others stay in registers according to spec.
        Structure sizes above are determined by their register size(16-byte fits into one R*X, 32-byte fits into two R*X, no matter the actual size).
        The structures don't get cross-packed in the registers, which means they can't overlap, even if possible on a bit level.
        Check .IsRef and pray it's true (don't need to handle struct fields individually, it's a pointer)
        */

        /*
        MSVC doesn't need any special code to be implemented.
        */

        if (ctx.AppContext.Binary is PE)
        {
            /*
            MSVC cconv:
                RCX = XMM0
                RDX = XMM1
                R8 = XMM2
                R9 = XMM3
                [stack, every field is 8 incl. f & d, uses mov]
            */

            var i = 0;

            if (isReturningAnOversizedStructure)
            {
                args.Add(ToOperand(MicrosoftNormalRegister.rcx + i));
                i++;
            }

            if (addThis)
            {
                args.Add(ToOperand(MicrosoftNormalRegister.rcx + i));
                i++;
            }

            void AddParameter(ParameterAnalysisContext? par)
            {
                if (i < 4)
                {
                    args.Add((par != null && IsXMM(par)) ? ToOperand(LinuxFloatingRegister.xmm0 + i) : ToOperand(MicrosoftNormalRegister.rcx + i));
                }
                else
                {
                    args.Add(InstructionSetIndependentOperand.MakeStack((i - 4) * ptrSize));
                }
            }

            for (; i < ctx.ParameterCount; i++)
            {
                AddParameter(ctx.Parameters[i]);
            }

            AddParameter(null); // The MethodInfo argument
        }
        else // if (ctx.AppContext.Binary is ElfFile)
        {
            /*
				GCC cconv (-O2):
					Integers & Longs:
						rdi
						rsi
						rdx
						rcx
						r8
						r9
						[stack, uses push]
					Doubles:
						xmm0
						xmm1
						xmm2
						xmm3
						xmm4
						xmm5
						xmm6
						xmm7
						[stack, uses push]
			*/

            LinuxNormalRegister nreg = 0;
            LinuxFloatingRegister freg = 0;
            var stack = 0;

            void AddParameter(ParameterAnalysisContext? par)
            {
                if (par != null && IsXMM(par))
                {
                    if (freg == LinuxFloatingRegister.Stack)
                    {
                        args.Add(InstructionSetIndependentOperand.MakeStack(stack));
                        stack += ptrSize;
                    }
                    else args.Add(ToOperand(freg++));
                }
                else
                {
                    if (nreg == LinuxNormalRegister.Stack)
                    {
                        args.Add(InstructionSetIndependentOperand.MakeStack(stack));
                        stack += ptrSize;
                    }
                    else args.Add(ToOperand(nreg++));
                }
            }

            if (isReturningAnOversizedStructure)
            {
                args.Add(ToOperand(nreg++));
            }

            if (addThis)
            {
                args.Add(ToOperand(nreg++));
            }

            foreach (var par in ctx.Parameters)
            {
                AddParameter(par);
            }

            AddParameter(null); // The MethodInfo argument
        }
        // else throw new NotSupportedException($"Resolution of 64-bit calling conventions is not supported for this binary type.");

        return args.ToArray();
    }

    private static InstructionSetIndependentOperand ToOperand(MicrosoftNormalRegister Reg) => Reg switch
    {
        MicrosoftNormalRegister.rcx => InstructionSetIndependentOperand.MakeRegister("rcx"),
        MicrosoftNormalRegister.rdx => InstructionSetIndependentOperand.MakeRegister("rdx"),
        MicrosoftNormalRegister.r8 => InstructionSetIndependentOperand.MakeRegister("r8"),
        MicrosoftNormalRegister.r9 => InstructionSetIndependentOperand.MakeRegister("r9"),
        _ => throw new InvalidOperationException("Went past the register limit during resolution.")
    };

    private static InstructionSetIndependentOperand ToOperand(LinuxNormalRegister Reg) => Reg switch
    {
        LinuxNormalRegister.rdi => InstructionSetIndependentOperand.MakeRegister("rdi"),
        LinuxNormalRegister.rsi => InstructionSetIndependentOperand.MakeRegister("rsi"),
        LinuxNormalRegister.rdx => InstructionSetIndependentOperand.MakeRegister("rdx"),
        LinuxNormalRegister.rcx => InstructionSetIndependentOperand.MakeRegister("rcx"),
        LinuxNormalRegister.r8 => InstructionSetIndependentOperand.MakeRegister("r8"),
        LinuxNormalRegister.r9 => InstructionSetIndependentOperand.MakeRegister("r9"),
        _ => throw new InvalidOperationException("Went past the register limit during resolution.")
    };

    private static InstructionSetIndependentOperand ToOperand(LinuxFloatingRegister Reg) => Reg switch
    {
        LinuxFloatingRegister.xmm0 => InstructionSetIndependentOperand.MakeRegister("xmm0"),
        LinuxFloatingRegister.xmm1 => InstructionSetIndependentOperand.MakeRegister("xmm1"),
        LinuxFloatingRegister.xmm2 => InstructionSetIndependentOperand.MakeRegister("xmm2"),
        LinuxFloatingRegister.xmm3 => InstructionSetIndependentOperand.MakeRegister("xmm3"),
        LinuxFloatingRegister.xmm4 => InstructionSetIndependentOperand.MakeRegister("xmm4"),
        LinuxFloatingRegister.xmm5 => InstructionSetIndependentOperand.MakeRegister("xmm5"),
        LinuxFloatingRegister.xmm6 => InstructionSetIndependentOperand.MakeRegister("xmm6"),
        LinuxFloatingRegister.xmm7 => InstructionSetIndependentOperand.MakeRegister("xmm7"),
        _ => throw new InvalidOperationException("Went past the register limit during resolution.")
    };

    private enum MicrosoftNormalRegister
    {
        rcx, rdx, r8, r9
    }

    private enum LinuxNormalRegister
    {
        rdi, rsi, rdx, rcx, r8, r9, Stack
    }

    private enum LinuxFloatingRegister
    {
        xmm0, xmm1, xmm2, xmm3, xmm4, xmm5, xmm6, xmm7, Stack
    }
}
