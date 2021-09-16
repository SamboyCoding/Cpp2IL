using Cpp2IL.Core.Analysis.Actions.ARM64;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;

namespace Cpp2IL.Core.Analysis
{
    public partial class AsmAnalyzerArmV8A
    {
        protected override void PerformInstructionChecks(Arm64Instruction instruction)
        {
            switch (instruction.Details.Operands.Length)
            {
                case 0:
                    CheckForZeroOpInstruction(instruction);
                    break;
                case 1:
                    CheckForSingleOpInstruction(instruction);
                    break;
                case 2:
                    CheckForTwoOpInstruction(instruction);
                    break;
            }
        }

        private void CheckForZeroOpInstruction(Arm64Instruction instruction)
        {
        }

        private void CheckForSingleOpInstruction(Arm64Instruction instruction)
        {
            var op0 = instruction.Details.Operands[0]!;
            var t0 = op0.Type;
            var r0 = op0.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;
            var r0Name = Utils.GetRegisterNameNew(r0);
            var var0 = Analysis.GetOperandInRegister(r0Name);
            var imm0 = op0.ImmediateSafe();
            
            var memoryBase = instruction.MemoryBase();
            var memoryOffset = instruction.MemoryOffset();
            var memoryIndex = instruction.MemoryIndex();

            switch (instruction.Mnemonic)
            {
                case "b":
                case "bl":
                    //Branch(-Link). Analogous to a JMP(/CALL) in x86.
                    var jumpTarget = (ulong) imm0;
                    if (SharedState.MethodsByAddress.TryGetValue(jumpTarget, out var managedFunctionBeingCalled))
                    {
                        Analysis.Actions.Add(new Arm64ManagedFunctionCallAction(Analysis, instruction));
                    }
                    
                    //If we're a b, we need a return too
                    if(instruction.Mnemonic == "b")
                        Analysis.Actions.Add(new Arm64ReturnAction(Analysis, instruction));
                    break;
            }
        }

        private void CheckForTwoOpInstruction(Arm64Instruction instruction)
        {
            var op0 = instruction.Details.Operands[0]!;
            var op1 = instruction.Details.Operands[1]!;

            var t0 = op0.Type;
            var t1 = op1.Type;

            var r0 = op0.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;
            var r1 = op1.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;

            var r0Name = Utils.GetRegisterNameNew(r0);
            var r1Name = Utils.GetRegisterNameNew(r1);

            var var0 = Analysis.GetOperandInRegister(r0Name);
            var var1 = Analysis.GetOperandInRegister(r1Name);

            var imm0 = op0.ImmediateSafe();
            var imm1 = op1.ImmediateSafe();

            var memoryBase = instruction.MemoryBase()?.Id ?? Arm64RegisterId.Invalid;
            var memoryOffset = instruction.MemoryOffset();
            var memoryIndex = instruction.MemoryIndex()?.Id ?? Arm64RegisterId.Invalid;

            var memVar = Analysis.GetOperandInRegister(Utils.GetRegisterNameNew(memoryBase)); 

            //The single most annoying part about capstone is that its mnemonics are strings.
            switch (instruction.Mnemonic)
            {
                case "mov" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Register && r1Name == "xzr":
                    //Move zero register to other register
                    Analysis.Actions.Add(new Arm64ZeroRegisterToRegisterAction(Analysis, instruction));
                    break;
                case "mov" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Register && var1 is {}:
                    //Move generic analyzed op to another reg
                    Analysis.Actions.Add(new Arm64RegCopyAction(Analysis, instruction));
                    break;
                case "ldr" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Memory && memVar is LocalDefinition && memoryOffset != 0:
                    //Field read - non-zero memory offset on local to register.
                    Analysis.Actions.Add(new Arm64FieldReadToRegAction(Analysis, instruction));
                    break;
                case "cmp":
                    Analysis.Actions.Add(new Arm64ComparisonAction(Analysis, instruction));
                    break;
                case "cbz":
                    //Compare *and* branch if 0
                    //But, skip the second op in the comparison, because it's the address to jump to.
                    Analysis.Actions.Add(new Arm64ComparisonAction(Analysis, instruction, true));
                    Analysis.Actions.Add(new Arm64JumpIfZeroOrNullAction(Analysis, instruction, 1));
                    break;
                case "cbnz":
                    //Compare and branch if non-0
                    //Again, skip the second op in the comparison, because it's the address to jump to.
                    Analysis.Actions.Add(new Arm64ComparisonAction(Analysis, instruction, true));
                    Analysis.Actions.Add(new Arm64JumpIfNonZeroOrNonNullAction(Analysis, instruction, 1));
                    break;
            }
        }
    }
}