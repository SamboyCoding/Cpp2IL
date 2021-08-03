//Feature flags

#define DEBUG_PRINT_OPERAND_DATA

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Analysis.PostProcessActions;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Code = Iced.Intel.Code;
using Instruction = Iced.Intel.Instruction;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Cpp2IL.Core.Analysis
{
    internal partial class AsmAnalyzer
    {
        public static int SUCCESSFUL_METHODS = 0;
        public static int FAILED_METHODS = 0;

        private readonly MethodDefinition? _methodDefinition;
        private ulong _methodEnd;
        private readonly KeyFunctionAddresses _keyFunctionAddresses;
        private readonly Il2CppBinary _cppAssembly;

        private readonly StringBuilder _methodFunctionality = new StringBuilder();
        private readonly InstructionList _instructions;

        internal List<TypeDefinition> AttributesForRestoration;

        private static readonly Mnemonic[] MNEMONICS_INDICATING_CONSTANT_IS_NOT_CONSTANT =
        {
            Mnemonic.Add, Mnemonic.Sub
        };

        private bool isGenuineMethod;

        internal readonly MethodAnalysis Analysis;

        internal AsmAnalyzer(ulong methodPointer, InstructionList instructions, KeyFunctionAddresses keyFunctionAddresses)
        {
            _keyFunctionAddresses = keyFunctionAddresses;
            _cppAssembly = LibCpp2IlMain.Binary!;
            _instructions = instructions;

            var instructionWhichOverran = FindInstructionWhichOverran(out var idx);

            if (instructionWhichOverran != default)
            {
                _instructions = new InstructionList(_instructions.Take(idx).ToList());
            }

            _methodEnd = _instructions.LastOrDefault().NextIP;
            if (_methodEnd == 0) _methodEnd = methodPointer;

            Analysis = new MethodAnalysis(methodPointer, _methodEnd, _instructions, keyFunctionAddresses);
            Analysis.OnExpansionRequested += AnalysisRequestedExpansion;
        }

        internal AsmAnalyzer(MethodDefinition methodDefinition, ulong methodStart, KeyFunctionAddresses keyFunctionAddresses)
        {
            _methodDefinition = methodDefinition;

            _methodDefinition.Body = new MethodBody(_methodDefinition);

            _keyFunctionAddresses = keyFunctionAddresses;
            _cppAssembly = LibCpp2IlMain.Binary!;

            //Pass 0: Disassemble
            _instructions = LibCpp2ILUtils.DisassembleBytesNew(LibCpp2IlMain.Binary!.is32Bit, methodDefinition.AsUnmanaged().CppMethodBodyBytes, methodStart);

            var instructionWhichOverran = FindInstructionWhichOverran(out var idx);

            if (instructionWhichOverran != default)
            {
                _instructions = new InstructionList(_instructions.Take(idx).ToList());
            }

            _methodEnd = _instructions.LastOrDefault().NextIP;
            if (_methodEnd == 0) _methodEnd = methodStart;

            isGenuineMethod = true;

            Analysis = new MethodAnalysis(_methodDefinition, methodStart, _methodEnd, _instructions, keyFunctionAddresses);
            Analysis.OnExpansionRequested += AnalysisRequestedExpansion;
        }

        internal void AddParameter(TypeDefinition type, string name)
        {
            Analysis.AddParameter(new ParameterDefinition(name, ParameterAttributes.None, type));
        }

        private void AnalysisRequestedExpansion(ulong ptr)
        {
            var newInstructions = Utils.GetMethodBodyAtVirtAddressNew(ptr, false);

            // var instructionWhichOverran = FindInstructionWhichOverran(out var idx);
            //
            // if (instructionWhichOverran != default)
            // {
            //     newInstructions = new InstructionList(newInstructions.Take(idx).ToList());
            // }

            _methodEnd = newInstructions.LastOrDefault().NextIP;
            _instructions.AddRange(newInstructions);
            Analysis.AbsoluteMethodEnd = _methodEnd;
        }

        private Instruction FindInstructionWhichOverran(out int idx)
        {
            var instructionWhichOverran = new Instruction();
            idx = 1;
            foreach (var i in _instructions.Skip(1))
            {
                idx++;
                if (SharedState.MethodsByAddress.ContainsKey(i.IP) || LibCpp2IlMain.Binary!.AllCustomAttributeGenerators.Contains(i.IP))
                {
                    instructionWhichOverran = i;
                    break;
                }

                if (i.Code == Code.Int3)
                {
                    instructionWhichOverran = i;
                    break;
                }
            }

            return instructionWhichOverran;
        }

        internal StringBuilder GetFullDumpNoIL()
        {
            var builder = new StringBuilder();

            builder.Append(GetAssemblyDump());
            builder.Append(GetWordyFunctionality());
            builder.Append(GetPseudocode());

            return builder;
        }

        internal StringBuilder GetAssemblyDump()
        {
            var builder = new StringBuilder();

            builder.Append($"Method: {_methodDefinition?.FullName}:");

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

        internal StringBuilder GetWordyFunctionality()
        {
            var builder = new StringBuilder();

            builder.Append($"\n\tMethod Synopsis For {(_methodDefinition?.IsStatic == true ? "Static " : "")}Method ")
                .Append(_methodDefinition?.FullName ?? "[unknown name]")
                .Append(":\n")
                .Append(_methodFunctionality)
                .Append("\n\n");

            return builder;
        }

        internal StringBuilder GetPseudocode()
        {
            var builder = new StringBuilder();

            builder.Append("\n\tGenerated Pseudocode:\n\n");

            //Preamble
            builder.Append($"\tDeclaring Type: {_methodDefinition?.DeclaringType.FullName ?? "unknown"}\n");
            builder.Append('\t').Append(_methodDefinition?.IsStatic == true ? "static " : "").Append(_methodDefinition?.ReturnType.FullName).Append(' ') //Staticness and return type
                .Append(_methodDefinition?.Name).Append('(') //Name and opening paranthesis
                .Append(string.Join(", ", _methodDefinition?.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}") ?? new List<string>())) //Parameters
                .Append(')').Append('\n'); //Closing parenthesis and new line.

            //Actions
            Analysis.Actions
                .Where(action => action.IsImportant()) //Action requires pseudocode generation
                .Select(action => $"{(action.PseudocodeNeedsLinebreakBefore() ? "\n" : "")}\t\t{"    ".Repeat(action.IndentLevel)}{action.ToPsuedoCode()?.Replace("\n", "\n" + "    ".Repeat(action.IndentLevel + 2))}") //Generate it 
                .Where(code => !string.IsNullOrWhiteSpace(code)) //Check it's valid
                .ToList()
                .ForEach(code => builder.Append(code).Append('\n')); //Append

            builder.Append("\n\n");

            return builder;
        }

        internal StringBuilder BuildILToString()
        {
            var builder = new StringBuilder();

            //IL Generation
            //Anyone reading my commits: This is a *start*. It's nowhere near done.
            var body = _methodDefinition!.Body;
            var processor = body.GetILProcessor();

            var originalBody = body.Instructions.ToList();
            var originalVariables = body.Variables.ToList();
            
            processor.Clear();

            builder.Append("Generated IL:\n\t");

            var success = true;

            foreach (var localDefinition in Analysis.Locals.Where(localDefinition => localDefinition.ParameterDefinition == null && localDefinition.Type != null))
            {
                try
                {
                    var varType = localDefinition.Type!;

                    if (varType is GenericInstanceType git)
                        varType = processor.ImportRecursive(git, _methodDefinition);
                    
                    localDefinition.Variable = new VariableDefinition(processor.ImportReference(varType, _methodDefinition));
                    body.Variables.Add(localDefinition.Variable);
                }
                catch (InvalidOperationException)
                {
                    Logger.WarnNewline($"Skipping IL Generation for {_methodDefinition}, as one of its locals, {localDefinition.Name}, has a type, {localDefinition.Type}, which is invalid for use in a variable.", "Analysis");
                    builder.Append($"IL Generation Skipped due to invalid local {localDefinition.Name} of type {localDefinition.Type}\n\t");
                    success = false;
                    break;
                }
            }

            if (success)
            {
                foreach (var action in Analysis.Actions.Where(i => i.IsImportant()))
                {
                    try
                    {
                        var il = action.ToILInstructions(Analysis, processor);

                        foreach (var instruction in il)
                        {
                            processor.Append(instruction);
                        }
                        
                        if(MethodAnalysis.ActionsWhichGenerateNoIL.Contains(action.GetType()))
                            continue;

                        var jumpsToHere = Analysis.JumpTargetsToFixByAction.Keys.Where(jt => jt.AssociatedInstruction.IP <= action.AssociatedInstruction.IP).ToList();
                        if (jumpsToHere.Count > 0)
                        {
                            var first = il.First();
                            foreach (var instruction in jumpsToHere.SelectMany(jumpDestAction => Analysis.JumpTargetsToFixByAction[jumpDestAction]))
                            {
                                instruction.Operand = first;
                            }
                        }
                        jumpsToHere.ForEach(key => Analysis.JumpTargetsToFixByAction.Remove(key));
                    }
                    catch (NotImplementedException)
                    {
                        builder.Append($"Don't know how to write IL for {action.GetType()}. Aborting here.\n");
                        success = false;
                        break;
                    }
                    catch (TaintedInstructionException e)
                    {
                        var message = e.ActualMessage ?? "No further info";
                        builder.Append($"Action of type {action.GetType()} is corrupt ({message}) and cannot be created as IL. Aborting here.\n");
                        success = false;
                        break;
                    }
                    catch (Exception e)
                    {
                        builder.Append($"Action of type {action.GetType()} threw an exception while generating IL. Aborting here.\n");
                        Logger.WarnNewline($"Exception generating IL for {_methodDefinition.FullName}, thrown by action {action.GetType().Name}, associated instruction {action.AssociatedInstruction}: {e}");
                        success = false;
                        break;
                    }
                }
            }

            if (body.Variables.Any(l => l.VariableType is GenericParameter {Position: -1}))
                //don't save to body if any locals are screwed.
                success = false;

            if (!success)
            {
                body.Variables.Clear();
                processor.Clear();
                originalVariables.ForEach(body.Variables.Add);
                originalBody.ForEach(processor.Append);
            }
            else
            {
                body.Optimize();
                
                builder.Append(string.Join("\n\t", body.Instructions))
                    .Append("\n\t");
            }

            if (isGenuineMethod)
            {
                if (success)
                    SUCCESSFUL_METHODS++;
                else
                    FAILED_METHODS++;
            }

            builder.Append("\n\n");

            return builder;
        }

        internal void RunPostProcessors()
        {
            new RemovedUnusedLocalsPostProcessor().PostProcess(Analysis, _methodDefinition!);
            new RenameLocalsPostProcessor().PostProcess(Analysis, _methodDefinition!);
        }

        internal void BuildMethodFunctionality()
        {
            _methodFunctionality.Append($"\t\tEnd of function at 0x{_methodEnd:X}\n\t\tAbsolute End is at 0x{Analysis.AbsoluteMethodEnd:X}\n");

            _methodFunctionality.Append("\t\tIdentified Jump Destination addresses:\n").Append(string.Join("\n", Analysis.IdentifiedJumpDestinationAddresses.Select(s => $"\t\t\t0x{s:X}"))).Append('\n');
            var lastIfAddress = 0UL;
            foreach (var action in Analysis.Actions)
            {
                if (Analysis.IdentifiedJumpDestinationAddresses.FirstOrDefault(s => s <= action.AssociatedInstruction.IP && s > lastIfAddress) is var jumpDestinationAddress && jumpDestinationAddress != 0)
                {
                    var associatedIfForThisElse = Analysis.GetAddressOfAssociatedIfForThisElse(jumpDestinationAddress);
                    var elseStart = Analysis.GetAddressOfElseThisIsTheEndOf(jumpDestinationAddress);
                    var ifStart = Analysis.GetAddressOfIfBlockEndingHere(jumpDestinationAddress);
                    if (associatedIfForThisElse != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tElse Block (starting at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") for Comparison at 0x")
                            .Append(associatedIfForThisElse.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else if (elseStart != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tEnd Of If-Else Block (at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") where the else started at 0x")
                            .Append(elseStart.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else if (ifStart != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tEnd Of If Block (at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") where the if started at 0x")
                            .Append(ifStart.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else
                    {
                        _methodFunctionality.Append("\n\t\tJump Destination (0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append("):\n");
                    }

                    lastIfAddress = jumpDestinationAddress;
                }

                if (Analysis.ProbableLoopStarts.FirstOrDefault(s => s <= action.AssociatedInstruction.IP && s > lastIfAddress) is { } loopAddress && loopAddress != 0)
                {
                    _methodFunctionality.Append("\n\t\tPotential Loop Start (0x")
                        .Append(loopAddress.ToString("x8").ToUpperInvariant())
                        .Append("):\n");

                    lastIfAddress = loopAddress;
                }

                string synopsisEntry;
                try
                {
                    synopsisEntry = action.GetSynopsisEntry();
                }
                catch (Exception e)
                {
                    Logger.WarnNewline($"Failed to generate synopsis for method {_methodDefinition?.FullName}, action of type {action.GetType().Name} for instruction {action.AssociatedInstruction} at 0x{action.AssociatedInstruction.IP:X} - got exception {e}");
                    throw new AnalysisExceptionRaisedException("Exception generating synopsis entry", e);
                }

                if (!string.IsNullOrWhiteSpace(synopsisEntry))
                {
                    _methodFunctionality.Append("\t\t0x")
                        .Append(action.AssociatedInstruction.IP.ToString("X8").ToUpperInvariant())
                        .Append(": ")
                        .Append(action.GetSynopsisEntry())
                        .Append('\n');
                }
            }
        }

        /// <summary>
        /// Performs analysis in order to populate the Action list. Doesn't generate any text. 
        /// </summary>
        /// <exception cref="AnalysisExceptionRaisedException">If an unhandled exception occurs while analyzing.</exception>
        internal void AnalyzeMethod()
        {
            //On windows x86_64, function params are passed RCX RDX R8 R9, then the stack
            //If these are floating point numbers, they're put in XMM0 to 3
            //Register eax/rax/whatever you want to call it is the return value (both of any functions called in this one and this function itself)

            //Main instruction loop
            for (var index = 0; index < _instructions.Count; index++)
            {
                var instruction = _instructions[index];
                try
                {
                    PerformInstructionChecks(instruction);
                }
                catch (Exception e)
                {
                    Logger.WarnNewline($"Failed to perform analysis on method {_methodDefinition?.FullName}\nWhile analysing instruction {instruction} at 0x{instruction.IP:X}\nGot exception: {e}\n", "Analyze");
                    throw new AnalysisExceptionRaisedException("Internal analysis exception", e);
                }
            }
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