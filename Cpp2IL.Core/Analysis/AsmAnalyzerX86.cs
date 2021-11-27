//Feature flags

#define DEBUG_PRINT_OPERAND_DATA

using System.Linq;
using System.Text;
using Cpp2IL.Core.Analysis.PostProcessActions;
using Cpp2IL.Core.Analysis.PostProcessActions.ILPostProcess;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace Cpp2IL.Core.Analysis
{
    public partial class AsmAnalyzerX86 : AsmAnalyzerBase<Instruction>
    {
        public static int SUCCESSFUL_METHODS = 0;
        public static int FAILED_METHODS = 0;

        private static readonly Mnemonic[] MNEMONICS_INDICATING_CONSTANT_IS_NOT_CONSTANT =
        {
            Mnemonic.Add, Mnemonic.Sub
        };

        internal AsmAnalyzerX86(ulong methodPointer, InstructionList instructions, BaseKeyFunctionAddresses keyFunctionAddresses) : base(methodPointer, instructions, keyFunctionAddresses) { }

        internal AsmAnalyzerX86(MethodDefinition methodDefinition, BaseKeyFunctionAddresses keyFunctionAddresses)
            : base(methodDefinition, methodDefinition.AsUnmanaged().MethodPointer, X86Utils.GetMethodBodyAtVirtAddressNew(methodDefinition.AsUnmanaged().MethodPointer, false), keyFunctionAddresses) { }

        protected override void AnalysisRequestedExpansion(ulong ptr)
        {
            var newInstructions = X86Utils.GetMethodBodyAtVirtAddressNew(ptr, false);

            MethodEnd = newInstructions.LastOrDefault().NextIP;
            _instructions.AddRange(newInstructions);
            Analysis.AbsoluteMethodEnd = MethodEnd;
        }

        protected override bool FindInstructionWhichOverran(out int idx)
        {
            idx = 1;
            foreach (var i in _instructions.Skip(1))
            {
                idx++;

                if (i.Code == Code.Int3)
                {
                    return true;
                }

                if (MethodDefinition != null && SharedState.MethodsByAddress.ContainsKey(i.IP))
                {
                    //Have method def - so are managed method - and hit another one here.
                    return true;
                }
                if (MethodDefinition == null && SharedState.AttributeGeneratorStarts.Contains(i.IP))
                {
                    //We have no method def - so are an attrib gen - and another attrib gen here.
                    return true;
                }
            }

            return false;
        }

        internal override StringBuilder GetAssemblyDump()
        {
            var builder = new StringBuilder();

            builder.Append($"Method: {MethodDefinition?.FullName}:");

            builder.Append("\tMethod Body (x86 ASM):\n");

            foreach (var instruction in _instructions)
            {
                var line = new StringBuilder();
                line.Append("0x").Append(instruction.IP.ToString("X8").ToUpperInvariant()).Append(' ').Append(instruction);

                //Dump debug data
#if DEBUG_PRINT_OPERAND_DATA
                line.Append("\t\t; DEBUG: {").Append(instruction.Op0Kind).Append('}').Append('/').Append(instruction.Op0Register).Append(' ');
                line.Append('{').Append(instruction.Op1Kind).Append('}').Append('/').Append(instruction.Op1Register).Append(" ||| ");
                line.Append(instruction.MemoryBase).Append(" | ").Append(instruction.MemoryDisplacement64).Append(" | ").Append(instruction.MemoryIndex);
                line.Append(" ||| ").Append(instruction.Op0Kind.IsImmediate() ? instruction.GetImmediate(0).ToString() : "N/A").Append(" | ").Append(instruction.Op1Kind.IsImmediate() ? instruction.GetImmediate(1).ToString() : "N/A");
#endif

                //I'm doing this here because it saves a bunch of effort later. Upscale all registers from 32 to 64-bit accessors. It's not correct, but it's simpler.
                // line = Utils.UpscaleRegisters(line);

                builder.Append("\t\t").Append(line); //write the current disassembled instruction to the type dump

                builder.Append('\n');
            }

            return builder;
        }

        public override void RunActionPostProcessors()
        {
            new RemovedUnusedLocalsPostProcessor<Instruction>().PostProcess(Analysis);
            new RenameLocalsPostProcessor<Instruction>().PostProcess(Analysis);
        }
        public override void RunILPostProcessors(Mono.Cecil.Cil.MethodBody body)
        {
            new RestoreConstReferences<Instruction>().PostProcess(Analysis, body);
            new OptimiseLocalsPostProcessor<Instruction>().PostProcess(Analysis, body);
        }

#if false
        private void CheckForArithmeticOperations(Instruction instruction)
        {
            if (instruction.Mnemonic == ud_mnemonic_code.UD_Iimul && instruction.Operands.Length <= 3 && instruction.Operands.Length > 0)
            {
                string destReg;
                string firstArgName;
                string secondArgName;

                switch (instruction.Operands.Length)
                {
                    case 1:
                        destReg = "rdx";
                        if (!_registerAliases.TryGetValue("rax", out firstArgName))
                            firstArgName = $"[value in rax]";

                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);
                        break;
                    case 2:
                        destReg = Utils.GetRegisterName(instruction.Operands[0]);
                        (firstArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);
                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                        // firstSourceReg = GetRegisterName(instruction.Operands[0]);
                        // secondSourceReg = GetRegisterName(instruction.Operands[1]);
                        break;
                    case 3:
                        destReg = Utils.GetRegisterName(instruction.Operands[0]);
                        // firstSourceReg = GetRegisterName(instruction.Operands[1]);
                        // secondSourceReg = GetRegisterName(instruction.Operands[2]);
                        (firstArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[2], instruction);
                        break;
                }

                var localName = $"local{_localNum}";
                _localNum++;

                _registerAliases[destReg] = localName;

                _typeDump.Append("; - identified and processed one of them there godforsaken imul instructions.");
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(LongReference.FullName).Append(" ").Append(localName).Append(" = ").Append(firstArgName).Append(" * ").Append(secondArgName).Append("\n");
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Multiplies {firstArgName} by {secondArgName} and stores the result in new local {localName} in register {destReg}\n");

                return;
            }

            //Need 2 operand
            if (instruction.Operands.Length < 2) return;

            //Needs to be a valid opcode
            if (!_inlineArithmeticOpcodes.Contains(instruction.Mnemonic) && !_localCreatingArithmeticOpcodes.Contains(instruction.Mnemonic)) return;

            var isInline = _inlineArithmeticOpcodes.Contains(instruction.Mnemonic);

            //The first operand is guaranteed to be a register
            var (name, type, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);

            //The second one COULD be a register, but it could also be a memory location containing a constant (say we're multiplying by 1.5, that'd be a constant)
            if (instruction.Operands[1].Type == ud_type.UD_OP_IMM || instruction.Operands[1].Type == ud_type.UD_OP_MEM && instruction.Operands[1].Base == ud_type.UD_R_RIP)
            {
                decimal constantValue;

                if (instruction.Operands[1].Type == ud_type.UD_OP_IMM)
                {
                    var immediateValue = Utils.GetImmediateValue(instruction, instruction.Operands[1]);
                    constantValue = immediateValue;
                    _typeDump.Append($" ; - Arithmetic operation between {name} and constant value: {constantValue}");
                }
                else
                {
                    var virtualAddress = Utils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]) + _methodStart;
                    _typeDump.Append($" ; - Arithmetic operation between {name} and constant stored at 0x{virtualAddress:X}");
                    long rawAddr = _cppAssembly.MapVirtualAddressToRaw(virtualAddress);
                    var constantBytes = _cppAssembly.raw.SubArray((int) rawAddr, 4);
                    var constantSingle = BitConverter.ToSingle(constantBytes, 0);
                    _typeDump.Append($" - constant is bytes: {BitConverter.ToString(constantBytes)}, or value {constantSingle}");
                    constantValue = (decimal) constantSingle;
                }

                if (isInline)
                {
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(constantValue).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) and {constantValue} (System.Decimal) in-place\n");
                }
                else
                {
                    var localName = $"local{_localNum}";
                    _localNum++;

                    var destReg = Utils.GetRegisterName(instruction.Operands[0]);

                    _registerAliases[destReg] = localName;
                    _registerTypes[destReg] = LongReference;
                    _registerContents.TryRemove(destReg, out _);

                    //System.Int64 localX = name [operation] constant\n
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(LongReference.FullName).Append(" ").Append(localName).Append(" = ").Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(constantValue).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) by {constantValue} (System.Decimal) and stores the result in new local {localName} in register {destReg}\n");
                }
            }
            else if (instruction.Operands[1].Type == ud_type.UD_OP_MEM || instruction.Operands[1].Type == ud_type.UD_OP_REG)
            {
                var (secondName, secondType, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                _typeDump.Append($" ; - Arithmetic operation between {name} and {secondName}");

                if (isInline)
                {
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(secondName).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) and {secondName} ({secondType}) in-place\n");
                }
                else
                {
                    var localName = $"local{_localNum}";
                    _localNum++;

                    var destReg = Utils.GetRegisterName(instruction.Operands[0]);

                    _registerAliases[destReg] = localName;
                    _registerTypes[destReg] = LongReference;
                    _registerContents.TryRemove(destReg, out _);

                    //System.Int64 localX = name [operation] constant\n
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(LongReference.FullName).Append(" ").Append(localName).Append(" = ").Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(secondName).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) by {secondName} ({secondType?.FullName}) and stores the result in new local {localName} in register {destReg}\n");
                }
            }
            else
            {
                _typeDump.Append($" ; Arithmetic operation between {name} and [unimplemented handler for operand type {instruction.Operands[1].Type}]");
            }
        }

#endif
    }
}
