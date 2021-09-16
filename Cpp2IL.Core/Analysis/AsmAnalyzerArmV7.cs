#define DEBUG_PRINT_OPERAND_DATA

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis
{
    public class AsmAnalyzerArmV7 : AsmAnalyzerBase<ArmInstruction>
    {
        private static List<ulong> _allKnownFunctionStarts;

        static AsmAnalyzerArmV7()
        {
            _allKnownFunctionStarts = LibCpp2IlMain.TheMetadata!.methodDefs.Select(m => m.MethodPointer).Concat(LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.Keys).ToList();
            //Sort in ascending order
            _allKnownFunctionStarts.Sort();
        }
        
        private static List<ArmInstruction> DisassembleInstructions(MethodDefinition definition)
        {
            var baseAddress = definition.AsUnmanaged().MethodPointer;
            
            //We can't use CppMethodBodyBytes to get the byte array, because ARMv7 doesn't have filler bytes like x86 does.
            //So we can't work out the end of the method.
            //But we can find the start of the next one!
            var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(_allKnownFunctionStarts.FirstOrDefault(a => a > baseAddress));
            var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(baseAddress);
            if (rawStartOfNextMethod < rawStart)
                rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;
            
            var bytes = LibCpp2IlMain.Binary.GetRawBinaryContent().Skip((int)rawStart).Take((int)(rawStartOfNextMethod - rawStart)).ToArray();

            var disassembler = CapstoneDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
            disassembler.EnableInstructionDetails = true;
            disassembler.DisassembleSyntax = DisassembleSyntax.Intel;

            return disassembler.Disassemble(bytes, (long)baseAddress).ToList();
        }

        public AsmAnalyzerArmV7(ulong methodPointer, IEnumerable<ArmInstruction> instructions) : base(methodPointer, instructions)
        {
        }

        public AsmAnalyzerArmV7(MethodDefinition definition, ulong methodPointer) : base(definition, methodPointer, DisassembleInstructions(definition))
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

            builder.Append("\tMethod Body (ARMv7 ASM):");

#if DEBUG_PRINT_OPERAND_DATA
            builder.Append("  {T0}/R0 {T1}/R1 ||| MBase | MOffset | MIndex ||| Imm0 | Imm1");
#endif
            builder.Append('\n');

            foreach (var instruction in _instructions)
            {
                var line = new StringBuilder();
                line.Append("0x").Append(instruction.Address.ToString("X8").ToUpperInvariant()).Append(' ').Append(instruction.Mnemonic).Append(' ').Append(instruction.Operand);

                //Dump debug data
#if DEBUG_PRINT_OPERAND_DATA
                line.Append("\t\t; DEBUG: {").Append(instruction.Details.Operands.GetValueSafely(0)?.Type).Append('}').Append('/').Append(instruction.Details.Operands.GetValueSafely(0)?.RegisterSafe()?.Name).Append(' ');
                line.Append('{').Append(instruction.Details.Operands.GetValueSafely(1)?.Type).Append('}').Append('/').Append(instruction.Details.Operands.GetValueSafely(1)?.RegisterSafe()?.Name).Append(" ||| ");
                line.Append(instruction.MemoryBase()?.Name).Append(" | ").Append(instruction.MemoryOffset()).Append(" | ").Append(instruction.MemoryIndex()?.Name);
                line.Append(" ||| ").Append(instruction.Details.Operands.GetValueSafely(0)?.ImmediateSafe().ToString() ?? "N/A").Append(" | ").Append(instruction.Details.Operands.GetValueSafely(1)?.ImmediateSafe().ToString() ?? "N/A");
#endif

                //I'm doing this here because it saves a bunch of effort later. Upscale all registers from 32 to 64-bit accessors. It's not correct, but it's simpler.
                // line = Utils.UpscaleRegisters(line);

                builder.Append("\t\t").Append(line); //write the current disassembled instruction to the type dump

                builder.Append('\n');
            }

            return builder;
        }

        public override void RunPostProcessors()
        {
            //no-op
        }

        protected override void PerformInstructionChecks(ArmInstruction instruction)
        {
            foreach (var armInstruction in _instructions)
            {
            }
        }
    }
}