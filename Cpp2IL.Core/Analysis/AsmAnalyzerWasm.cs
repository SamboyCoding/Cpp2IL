using System;
using System.Collections.Generic;
using System.Text;
using Cpp2IL.Core.Analysis.PostProcessActions;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Wasm;
using Mono.Cecil;
using WasmDisassembler;

namespace Cpp2IL.Core.Analysis
{
    public partial class AsmAnalyzerWasm : AsmAnalyzerBase<WasmInstruction>
    {
        private static List<WasmInstruction> DisassembleInstructions(WasmFunctionDefinition wasmFunc)
        {
            try
            {
                return Disassembler.Disassemble(wasmFunc.AssociatedFunctionBody!.Instructions, (uint) wasmFunc.AssociatedFunctionBody.InstructionsOffset);
            }
            catch (Exception e)
            {
                Logger.WarnNewline($"Disassembly has failed for a method. Its ghidra name is {WasmUtils.GetGhidraFunctionName(wasmFunc)}. The message was {e.Message}");
                return new()
                {
                    new()
                    {
                        Ip = (uint) wasmFunc.AssociatedFunctionBody!.InstructionsOffset,
                        Mnemonic = WasmMnemonic.Unreachable,
                    }
                };
            }
        }

        private readonly WasmFunctionDefinition _wasmDefinition;

        public AsmAnalyzerWasm(ulong methodPointer, IEnumerable<WasmInstruction> instructions, BaseKeyFunctionAddresses keyFunctionAddresses) : base(methodPointer, instructions, keyFunctionAddresses)
        {
        }

        private AsmAnalyzerWasm(MethodDefinition definition, WasmFunctionDefinition wasmDefinition, BaseKeyFunctionAddresses keyFunctionAddresses)
            : base(definition, (ulong) wasmDefinition.AssociatedFunctionBody!.InstructionsOffset, DisassembleInstructions(wasmDefinition), keyFunctionAddresses)
        {
            _wasmDefinition = wasmDefinition;
            if(_instructions.Count == 1 && _instructions[0].Mnemonic == WasmMnemonic.Unreachable)
                Logger.WarnNewline($"\tThe friendly name of the failed method is {definition.FullName}");
        }

        public AsmAnalyzerWasm(MethodDefinition definition, ulong methodPointer, BaseKeyFunctionAddresses baseKeyFunctionAddresses) : this(definition, WasmUtils.GetWasmDefinition(definition), baseKeyFunctionAddresses)
        {
        }

        protected override bool FindInstructionWhichOverran(out int idx)
        {
            //todo
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

            builder.Append($"Method: {MethodDefinition?.FullName}:\n");
            builder.Append($"Ghidra Name: {WasmUtils.GetGhidraFunctionName(_wasmDefinition)}\n");

            builder.Append("\tMethod Body (WebAssembly):");

#if DEBUG_PRINT_OPERAND_DATA
            builder.Append("  {T0}/R0 {T1}/R1 {T2}/R2 ||| MBase | MOffset | MIndex ||| Imm0 | Imm1 | Imm2");
#endif
            builder.Append('\n');

            foreach (var instruction in _instructions)
            {
                var line = new StringBuilder();
                line.Append(instruction);

                //Dump debug data
#if DEBUG_PRINT_OPERAND_DATA
                if (!instruction.IsSkippedData)
                {
                    line.Append("\t\t; DEBUG: ");
                    line.Append("{").Append(instruction.Details.Operands.GetValueSafely(0)?.Type).Append('}').Append('/').Append(instruction.Details.Operands.GetValueSafely(0)?.RegisterSafe()?.Name).Append(' ');
                    line.Append('{').Append(instruction.Details.Operands.GetValueSafely(1)?.Type).Append('}').Append('/').Append(instruction.Details.Operands.GetValueSafely(1)?.RegisterSafe()?.Name);
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

        public override void RunActionPostProcessors()
        {
            new RemovedUnusedLocalsPostProcessor<WasmInstruction>().PostProcess(Analysis);
            new RenameLocalsPostProcessor<WasmInstruction>().PostProcess(Analysis);
        }
        public override void RunILPostProcessors(Mono.Cecil.Cil.MethodBody body)
        {
            //no-op
        }
    }
}