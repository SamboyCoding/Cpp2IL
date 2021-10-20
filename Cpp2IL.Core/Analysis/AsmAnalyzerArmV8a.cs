#define DEBUG_PRINT_OPERAND_DATA
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Analysis.PostProcessActions;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis
{
    public partial class AsmAnalyzerArmV8A : AsmAnalyzerBase<Arm64Instruction>
    {
        private static List<Arm64Instruction> DisassembleInstructions(MethodDefinition definition)
        {
            var baseAddress = definition.AsUnmanaged().MethodPointer;

            return Utils.GetArm64MethodBodyAtVirtualAddress(baseAddress);
        }

        private string FunctionArgumentDump;

        public AsmAnalyzerArmV8A(ulong methodPointer, IEnumerable<Arm64Instruction> instructions, BaseKeyFunctionAddresses keyFunctionAddresses) : base(methodPointer, instructions, keyFunctionAddresses)
        {
        }

        public AsmAnalyzerArmV8A(MethodDefinition definition, ulong methodPointer, BaseKeyFunctionAddresses baseKeyFunctionAddresses) : base(definition, methodPointer, DisassembleInstructions(definition), baseKeyFunctionAddresses)
        {
            var builder = new StringBuilder();
            foreach (var (reg, operand) in Analysis.RegisterData)
            {
                builder.Append($"\t\t{operand} in {reg}\n");
            }

            FunctionArgumentDump = builder.ToString();
        }

        protected override bool FindInstructionWhichOverran(out int idx)
        {
            idx = _instructions.Count;
                
            return false;
        }

        protected override void AnalysisRequestedExpansion(ulong ptr)
        {
            //todo
        }

        internal override StringBuilder GetAssemblyDump()
        {
            var builder = new StringBuilder();

            builder.Append($"Method: {MethodDefinition?.FullName}:\n\n");

            builder.Append($"\tReturn value uses ARM64 slot: {Analysis.Arm64ReturnValueLocation}\n");

            builder.Append("\tFunction Parameter Dump:\n");

            builder.Append(FunctionArgumentDump).Append('\n');

            builder.Append("\tMethod Body (ARMv8a ASM):");

#if DEBUG_PRINT_OPERAND_DATA
            builder.Append("  {T0}/R0 {T1}/R1 {T2}/R2 ||| MBase | MOffset | MIndex ||| Imm0 | Imm1 | Imm2");
#endif
            builder.Append('\n');

            foreach (var instruction in _instructions)
            {
                var line = new StringBuilder();
                line.Append("0x").Append(instruction.Address.ToString("X8").ToUpperInvariant()).Append(' ').Append(instruction.Mnemonic).Append(' ').Append(instruction.Operand);

                //Dump debug data
#if DEBUG_PRINT_OPERAND_DATA
                if (!instruction.IsSkippedData)
                {
                    line.Append("\t\t; DEBUG: ");
                    line.Append("{").Append(instruction.Details.Operands.GetValueSafely(0)?.Type).Append('}').Append('/').Append(instruction.Details.Operands.GetValueSafely(0)?.RegisterSafe()?.Name).Append(' ');
                    line.Append('{').Append(instruction.Details.Operands.GetValueSafely(1)?.Type).Append('}').Append('/').Append(instruction.Details.Operands.GetValueSafely(1)?.RegisterSafe()?.Name).Append(' ');
                    line.Append('{').Append(instruction.Details.Operands.GetValueSafely(2)?.Type).Append('}').Append('/').Append(instruction.Details.Operands.GetValueSafely(2)?.RegisterSafe()?.Name);

                    line.Append(" ||| ");
                    line.Append(instruction.MemoryBase()?.Name).Append(" | ").Append(instruction.MemoryOffset()).Append(" | ").Append(instruction.MemoryIndex()?.Name);
                    line.Append(" ||| ");
                    line.Append(instruction.Details.Operands.GetValueSafely(0)?.ImmediateSafe().ToString() ?? "N/A").Append(" | ");
                    line.Append(instruction.Details.Operands.GetValueSafely(1)?.ImmediateSafe().ToString() ?? "N/A").Append(" | ");
                    line.Append(instruction.Details.Operands.GetValueSafely(2)?.ImmediateSafe().ToString() ?? "N/A");
                }
#endif

                builder.Append("\t\t").Append(line); //write the current disassembled instruction to the type dump

                builder.Append('\n');
            }

            return builder;
        }

        public override void RunPostProcessors()
        {
            new RemovedUnusedLocalsPostProcessor<Arm64Instruction>().PostProcess(Analysis);
            new RenameLocalsPostProcessor<Arm64Instruction>().PostProcess(Analysis);
        }
    }
}