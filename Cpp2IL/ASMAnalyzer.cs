using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    internal class AsmDumper
    {
        private static readonly Regex UpscaleRegex = new Regex("(?:^|([^a-zA-Z]))e([a-z]{2})", RegexOptions.Compiled);

        private readonly MethodDefinition _methodDefinition;
        private readonly ulong _methodStart;
        private ulong _methodEnd;
        private readonly List<AssemblyBuilder.GlobalIdentifier> _globals;
        private readonly KeyFunctionAddresses _keyFunctionAddresses;
        private readonly PE.PE _cppAssembly;
        private Dictionary<string, string> _registerAliases;
        private Dictionary<string, TypeReference> _registerTypes;
        private StringBuilder _methodFunctionality;
        private StringBuilder _psuedoCode = new StringBuilder();
        private StringBuilder _typeDump;
        private Dictionary<string, object> _registerContents;
        private List<Instruction> _instructions;
        private int _blockDepth;
        private int _localNum;
        private List<string> _loopRegisters;

        private Tuple<(string, TypeReference), (string, TypeReference)> _lastComparison;
        private List<int> _indentCounts = new List<int>();

        internal AsmDumper(MethodDefinition methodDefinition, CppMethodData method, ulong methodStart, List<AssemblyBuilder.GlobalIdentifier> globals, KeyFunctionAddresses keyFunctionAddresses, PE.PE cppAssembly)
        {
            _methodDefinition = methodDefinition;
            _methodStart = methodStart;
            _globals = globals;
            _keyFunctionAddresses = keyFunctionAddresses;
            _cppAssembly = cppAssembly;

            //Pass 0: Disassemble
            _instructions = Utils.DisassembleBytes(method.MethodBytes);
        }

        private List<Instruction> TrimOutIl2CppCrap(List<Instruction> instructions)
        {
            var ret = new List<Instruction>();

            for (var i = 0; i < instructions.Count; i++)
            {
                if (Utils.CheckForNullCheckAtIndex(_methodStart, _cppAssembly, instructions, i, _keyFunctionAddresses))
                {
                    //Skip this (TEST) and the following instruction (JZ)
                    i++;
                    continue;
                }

                var toSkip = Utils.CheckForInitCallAtIndex(_methodStart, instructions, i, _keyFunctionAddresses);
                if (toSkip != 0)
                {
                    //Need to preserve the MOV which is the value after this one if skipping 5.
                    if (toSkip == 5)
                        ret.Add(instructions[i + 1]);

                    //And skip the instructions
                    i += toSkip;
                    continue;
                }

                toSkip = Utils.CheckForStaticClassInitAtIndex(_methodStart, instructions, i, _keyFunctionAddresses);
                if (toSkip != 0)
                {
                    //No need to preserve anything, just skip
                    i += toSkip;
                    continue;
                }

                var insn = instructions[i];

                try
                {
                    //Single call to the bailout, sometimes left over at the end of a function
                    if (insn.Mnemonic == ud_mnemonic_code.UD_Icall && Utils.GetJumpTarget(insn, _methodStart + insn.PC) == _keyFunctionAddresses.AddrBailOutFunction)
                        continue;
                }
                catch (Exception)
                {
                    // ignored
                }

                //Need to clean up the syntax around native method lookups as it confuses the lexer
                //What it currently does is reads from a global storing the cached address of the function
                //And if it's not zero jumps over the lookup, straight to the call
                //And if it IS zero continues on to the lookup, a write to that global, and then the call.

                //To detect this, we look for a MOV from a relative offset from rip, into rax (i THINK it's always rax)
                //This is followed by a test against itself (checking for zero), then a jnz
                //Then it LEAs the literal name of the function (this we want to keep)
                //Then calls the native lookup (keep this too)
                //Then tests rax against itself again to check it was able to resolve the function
                //Then JZs down to a block that bails out 
                //Then moves the address to the global cache variable
                //Then we have the call, and this onwards we preserve.

                if (insn.Mnemonic == ud_mnemonic_code.UD_Imov && insn.Operands[1].Base == ud_type.UD_R_RIP && instructions.Count - i > 7)
                {
                    var requiredPattern = new[]
                    {
                        ud_mnemonic_code.UD_Imov, ud_mnemonic_code.UD_Itest, ud_mnemonic_code.UD_Ijnz, ud_mnemonic_code.UD_Ilea, ud_mnemonic_code.UD_Icall, ud_mnemonic_code.UD_Itest, ud_mnemonic_code.UD_Ijz, ud_mnemonic_code.UD_Imov
                    };
                    var patternInstructions = instructions.Skip(i).Take(8).ToArray();
                    var actualPattern = patternInstructions.Select(inst => inst.Mnemonic).ToArray();
                    // Console.WriteLine($"Expecting {string.Join(",", requiredPattern)}, got {string.Join(",", actualPattern)}");
                    if (actualPattern.SequenceEqual(requiredPattern))
                    {
                        //We need to preserve i+3 and i+4
                        ret.Add(instructions[i + 3]);
                        ret.Add(instructions[i + 4]);
                        i += 7;
                        continue;
                    }
                }

                ret.Add(insn);
            }

            return ret;
        }

        internal void AnalyzeMethod(StringBuilder typeDump, ref List<ud_mnemonic_code> allUsedMnemonics)
        {
            _typeDump = typeDump;
            _registerAliases = new Dictionary<string, string>();
            _registerTypes = new Dictionary<string, TypeReference>();
            _registerContents = new Dictionary<string, object>();

            //Map of jumped-to addresses to functionality summaries (for if statements)
            var jumpTable = new Dictionary<ulong, List<string>>();

            //As we're on windows, function params are passed RCX RDX R8 R9, then the stack
            //If these are floating point numbers, they're put in XMM0 to 3
            //Register eax/rax/whatever you want to call it is the return value (both of any functions called in this one and this function itself)

            typeDump.Append($"Method: {_methodDefinition.FullName}: (");

            _methodFunctionality = new StringBuilder();

            //Pass 1: Removal of unneeded generated code
            _instructions = TrimOutIl2CppCrap(_instructions);

            //Int3 is padding, we stop at the first one.
            var idx = _instructions.FindIndex(i => i.Mnemonic == ud_mnemonic_code.UD_Iint3);
            if (idx > 0)
                _instructions = _instructions.Take(idx + 1).ToList();

            var distinctMnemonics = new List<ud_mnemonic_code>(_instructions.Select(i => i.Mnemonic).Distinct());
            allUsedMnemonics = new List<ud_mnemonic_code>(allUsedMnemonics.Concat(distinctMnemonics).Distinct());

            //Do this AFTER the trim so we get a better result 
            _methodEnd = _methodStart + _instructions.Last().PC;

            //Pass 2: Early Loop Detection
            _loopRegisters = DetectPotentialLoops(_instructions);

            var counterNum = 1;
            var loopDetails = new List<string>();

            //Define counter variables for all loop registers
            foreach (var loopRegister in _loopRegisters)
            {
                _registerAliases[loopRegister] = $"counter{counterNum}";
                _registerTypes[loopRegister] = Utils.TryLookupTypeDefByName("System.Int32").Item1;
                loopDetails.Add($"counter{counterNum} in {loopRegister}");
                counterNum++;
            }

            //Flag up any loops we found in the summary
            if (_loopRegisters.Count > 0)
                _methodFunctionality.Append($"\t\tPotential Loops: {string.Join(",", loopDetails)}\n");

            //Dump params
            typeDump.Append("\tParameters in registers: \n");

            var registers = new List<string>(new[] {"rcx/xmm0", "rdx/xmm1", "r8/xmm2", "r9/xmm3"});
            var stackIndex = 0;

            //If not static add "this" reference to params
            if (!_methodDefinition.IsStatic)
            {
                var pos = registers[0];
                registers.RemoveAt(0);
                typeDump.Append($"\t\t{_methodDefinition.DeclaringType} this in register {pos}\n");
                foreach (var reg in pos.Split('/'))
                {
                    _registerAliases[reg] = "this";
                    _registerTypes[reg] = _methodDefinition.DeclaringType;
                }
            }

            //Handle all actual params
            foreach (var parameter in _methodDefinition.Parameters)
            {
                object pos;
                var isReg = false;
                if (registers.Count > 0)
                {
                    var regPair = registers[0];
                    pos = regPair;
                    isReg = true;
                    registers.RemoveAt(0);
                    foreach (var reg in regPair.Split('/'))
                    {
                        _registerAliases[reg] = parameter.Name;
                        _registerTypes[reg] = parameter.ParameterType;
                    }
                }
                else
                {
                    pos = stackIndex.ToString();
                    stackIndex += 8; //Pointer.
                }

                typeDump.Append($"\t\t{parameter.ParameterType.FullName} {parameter.Name} in {(isReg ? $"register {pos}" : $"stack at 0x{pos:X}")}\n");
            }

            typeDump.Append("\tMethod Body (x86 ASM):\n");

            _localNum = 1;
            _lastComparison = new Tuple<(string, TypeReference), (string, TypeReference)>(("", null), ("", null));
            var index = 0;

            _methodFunctionality.Append($"\t\tEnd of function at 0x{_methodEnd:X}\n");

            //Main instruction loop
            while (index < _instructions.Count - 1)
            {
                var instruction = _instructions[index];
                index++;

                _blockDepth = _indentCounts.Count;

                string line;

                //SharpDisasm is a godawful library, and it's not threadsafe (but only for instruction tostrings), but it's the best we've got. So don't do this in parallel.
                lock (Disassembler.Translator)
                    line = instruction.ToString();

                //I'm doing this here because it saves a bunch of effort later. Upscale all registers from 32 to 64-bit accessors. It's not correct, but it's simpler.
                line = UpscaleRegisters(line);

                //Apply any aliases to the line
                line = _registerAliases.Aggregate(line, (current, kvp) => current.Replace($" {kvp.Key}", $" {kvp.Value}_{kvp.Key}").Replace($"[{kvp.Key}", $"[{kvp.Value}_{kvp.Key}"));

                typeDump.Append($"\t\t{line}"); //write the current disassembled instruction to the type dump

                PerformInstructionChecks(instruction);

                typeDump.Append("\n");

                _indentCounts = _indentCounts
                    .Select(i => i - 1)
                    .Where(i => i != 0)
                    .ToList();
            }

            typeDump.Append($"\n\tMethod Synopsis:\n{_methodFunctionality}\n\n");

            typeDump.Append($"\n\tGenerated Pseudocode:\n\n{_psuedoCode}\n");
        }

        private void PerformInstructionChecks(Instruction instruction)
        {
            //Detect field writes
            CheckForFieldWrites(instruction);

            //And check for reads on (non-global) fields.
            CheckForFieldReads(instruction);

            //Check for XOR Reg, Reg
            CheckForRegClear(instruction);

            //Check for moving either a global or another register into the register referenced in the first operand.
            CheckForMoveIntoRegister(instruction);

            //Check for e.g. CALL rax
            CheckForCallRegister(instruction);

            //Check for a direct method call
            CheckForCallAddress(instruction);

            //Check for TEST or CMP statements
            CheckForConditions(instruction);

            //Check for e.g. JGE statements
            CheckForConditionalJumps(instruction);

            //Check for INC statements
            CheckForIncrements(instruction);

            //Check for floating-point arithmetic (MULSS, etc)
            CheckForArithmeticOperations(instruction);

            //Check for RET
            CheckForReturn(instruction);
        }

        private void HandleFunctionCall(MethodDefinition target, bool processReturnType, TypeReference returnType = null)
        {
            if (returnType == null)
                returnType = target.ReturnType;

            if (processReturnType && returnType != null && returnType.Name != "Void")
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(returnType.FullName).Append(" ").Append($"local{_localNum} = ");
            else
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth));


            _typeDump.Append($" - function {target.FullName}");
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Calls {(target.IsStatic ? "static" : "instance")} function {target.FullName}");
            var args = new List<string>();

            var paramRegisters = new List<string>(new[] {"rcx/xmm0", "rdx/xmm1", "r8/xmm2", "r9/xmm3"});
            if (!target.IsStatic)
            {
                //This param 
                var possibilities = paramRegisters.First().Split('/');
                paramRegisters.RemoveAt(0);
                foreach (var possibility in possibilities)
                {
                    if (!_registerAliases.ContainsKey(possibility)) continue;
                    _registerTypes.TryGetValue(possibility, out var type);

                    args.Add($"{_registerAliases[possibility]} (type {type?.Name}) as this in register {possibility}");
                    _psuedoCode.Append(_registerAliases[possibility] + "." + target.Name + "(");
                    break;
                }
            } else
                _psuedoCode.Append(target.DeclaringType.FullName + "." + target.Name + "(");

            foreach (var parameter in target.Parameters)
            {
                var possibilities = paramRegisters.First().Split('/');
                paramRegisters.RemoveAt(0);
                var success = false;
                foreach (var possibility in possibilities)
                {
                    if (!_registerAliases.ContainsKey(possibility))
                    {
                        //Could be a numerical value, check
                        if (parameter.ParameterType.IsPrimitive && _registerContents.ContainsKey(possibility) && _registerContents[possibility]?.GetType().IsPrimitive == true)
                        {
                            //Coerce if bool
                            if (parameter.ParameterType.Name == "Boolean")
                            {
                                args.Add($"{Convert.ToInt32(_registerContents[possibility]) != 0} (coerced to bool from {_registerContents[possibility]}) (type CONSTANT) as {parameter.Name} in register {possibility}");
                                _psuedoCode.Append(Convert.ToInt32(_registerContents[possibility]) != 0);
                            }
                            else
                            {
                                args.Add($"{_registerContents[possibility]} (type CONSTANT) as {parameter.Name} in register {possibility}");
                                _psuedoCode.Append(_registerContents[possibility]);
                            }

                            success = true;
                            if (target.Parameters.Last() != parameter)
                                _psuedoCode.Append(", ");
                            break;
                        }

                        //Check for null as a literal
                        if (!parameter.ParameterType.IsPrimitive && _registerContents.ContainsKey(possibility) && (_registerContents[possibility] as int?) is {} val && val == 0)
                        {
                            args.Add($"NULL (as a literal) as {parameter.Name} in register {possibility}");
                            _psuedoCode.Append("null");

                            success = true;
                            if (target.Parameters.Last() != parameter)
                                _psuedoCode.Append(", ");
                            break;
                        }

                        continue;
                    }

                    if (_registerAliases[possibility].StartsWith("global_LITERAL"))
                    {
                        var global = GetGlobalInReg(possibility);
                        if (global.HasValue)
                        {
                            args.Add($"'{global.Value.Name}' (LITERAL type System.String) as {parameter.Name} in register {possibility}");
                            _psuedoCode.Append($"'{global.Value.Name}'");
                            
                            success = true;
                            if (target.Parameters.Last() != parameter)
                                _psuedoCode.Append(", ");
                            break;
                        }
                    }

                    _registerTypes.TryGetValue(possibility, out var type);
                    args.Add($"{_registerAliases[possibility]} (type {type?.Name}) as {parameter.Name} in register {possibility}");
                    _psuedoCode.Append(_registerAliases[possibility]);
                    success = true;
                    if (target.Parameters.Last() != parameter)
                        _psuedoCode.Append(", ");
                    break;
                }

                if (!success)
                {
                    _psuedoCode.Append("<unknown>");
                    if (target.Parameters.Last() != parameter)
                        _psuedoCode.Append(", ");
                    args.Add($"<unknown> as {parameter.Name} in one of the registers {string.Join("/", possibilities)}");
                }

                if (paramRegisters.Count != 0) continue;

                args.Add(" ... and more, out of space in registers.");
                break;
            }

            _psuedoCode.Append(")\n");

            if (args.Count > 0)
                _methodFunctionality.Append($" with parameters: {string.Join(", ", args)}");
            _methodFunctionality.Append("\n");

            if (processReturnType && returnType != null && returnType.Name != "Void")
                PushMethodReturnTypeToLocal(returnType);
        }

        private void PushMethodReturnTypeToLocal(TypeReference returnType)
        {
            //Floating point => xmm0
            //Boolean => al
            var reg = returnType.Name == "Single" || returnType.Name == "Double" || returnType.Name == "Decimal"
                ? "xmm0"
                : returnType.Name == "Boolean"
                    ? "al"
                    : "rax";
            _registerTypes[reg] = returnType;
            _registerAliases[reg] = $"local{_localNum}";
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates local variable local{_localNum} of type {returnType.Name} and sets it to the return value\n");
            _localNum++;
        }

        private string GetRegisterName(Operand operand)
        {
            var theBase = operand.Base;
            return UpscaleRegisters(theBase.ToString().Replace("UD_R_", "").ToLower());
        }

        private string UpscaleRegisters(string replaceIn)
        {
            //TODO: Perf: this one liner is responsible for about 20% of the total execution time of the program. Can we speed it up?
            return UpscaleRegex.Replace(replaceIn, "$1r$2");
        }

        private List<string> DetectPotentialLoops(List<Instruction> instructions)
        {
            return instructions
                .Where(instruction => instruction.Mnemonic == ud_mnemonic_code.UD_Iinc)
                .Select(instruction => UpscaleRegisters(instruction.Operands[0].Base.ToString().Replace("UD_R_", "").ToLower()))
                .Distinct()
                .ToList();
        }

        private (string, TypeReference?) GetDetailsOfReferencedObject(Operand operand, Instruction i)
        {
            var theBase = operand.Base;
            var sourceReg = UpscaleRegisters(theBase.ToString().Replace("UD_R_", "").ToLower());
            string objectName = null;
            TypeReference objectType = null;
            switch (operand.Type)
            {
                case ud_type.UD_OP_MEM:
                    //Field read
                    if (GetFieldReferencedByOperand(operand) is { } field)
                    {
                        _registerAliases.TryGetValue(sourceReg, out var fieldReadAlias);
                        if (fieldReadAlias == null)
                            fieldReadAlias = $"the value in register {sourceReg}";
                        objectName = $"Field {field.Name} read from {fieldReadAlias}";
                        objectType = field.FieldType;
                        break;
                    }

                    //Check for global
                    var globalAddr = Utils.GetOffsetFromMemoryAccess(i, operand) + _methodStart;
                    if (_globals.Find(g => g.Offset == globalAddr) is {} glob && glob.Offset == globalAddr)
                        objectName = $"global_{glob.IdentifierType}_{glob.Name}";
                    else
                        objectName = $"[unknown global variable at 0x{globalAddr:X}]";
                    objectType = Utils.TryLookupTypeDefByName("System.Int64").Item1;
                    break;
                case ud_type.UD_OP_REG:
                    _registerAliases.TryGetValue(sourceReg, out var alias);
                    _registerContents.TryGetValue(sourceReg, out var constant);
                    _registerTypes.TryGetValue(sourceReg, out objectType);
                    objectType ??= Utils.TryLookupTypeDefByName(constant?.GetType().FullName).Item1;
                    objectName = alias ??
                                 (constant?.GetType().IsPrimitive == true
                                     ? constant.ToString()
                                     : $"[value in {sourceReg}]");
                    break;
                case ud_type.UD_OP_CONST:
                    objectName = $"0x{operand.LvalUDWord:X}";
                    objectType = Utils.TryLookupTypeDefByName("System.Int64").Item1;
                    break;
                case ud_type.UD_OP_IMM:
                    objectName = $"0x{Utils.GetIMMValue(i, operand):X}";
                    objectType = Utils.TryLookupTypeDefByName("System.Int64").Item1;
                    break;
            }

            return (objectName ?? $"<unknown refobject type = {operand.Type} constantval = {operand.LvalUDWord} base = {operand.Base}>", objectType);
        }

        private FieldDefinition GetFieldReferencedByOperand(Operand operand)
        {
            if (operand.Type != ud_type.UD_OP_MEM || operand.Base == ud_type.UD_R_RIP) return null;

            var sourceReg = GetRegisterName(operand);
            var offset = Utils.GetOperandMemoryOffset(operand);

            if (offset == 0 && _registerContents.ContainsKey(sourceReg) && _registerContents[sourceReg] is FieldDefinition fld)
                //This register contains a field definition. Return it
                return fld;

            var isStatic = offset >= 0xb8;
            if (!isStatic)
            {
                _registerTypes.TryGetValue(sourceReg, out var type);
                if (type == null) return null;

                //Read at offset in type
                var fieldNum = (int) (offset - 16) / 8;

                var typeDef = type.Resolve();
                if (typeDef == null)
                {
                    (typeDef, _) = Utils.TryLookupTypeDefByName(type.FullName);
                }

                if (typeDef == null) return null;

                var fields = typeDef.Fields.Where(f => f.Constant == null).ToList();
                if (fields.Count <= fieldNum || fieldNum < 0) return null;
                try
                {
                    return fields[fieldNum];
                }
                catch (Exception)
                {
                    return null;
                }
            }
            else
            {
                //If we're reading a static, check the global in the source reg
                _registerContents.TryGetValue(sourceReg, out var content);
                if (!(content is AssemblyBuilder.GlobalIdentifier global)) return null;


                //Ok we have a global, resolve it
                var (type, _) = Utils.TryLookupTypeDefByName(global.Name);
                if (type == null) return null;

                var fields = type.Fields.Where(f => f.IsStatic).ToList();
                var fieldNum = (int) (offset - 0xb8) / 8;

                try
                {
                    return fields[fieldNum];
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private string GetArithmeticOperationName(ud_mnemonic_code code)
        {
            switch (code)
            {
                case ud_mnemonic_code.UD_Imulss:
                    return "Multiplies";
                default:
                    return "[Unknown Operation Name]";
            }
        }

        private string GetArithmeticOperationAssignment(ud_mnemonic_code code)
        {
            switch (code)
            {
                case ud_mnemonic_code.UD_Imulss:
                    return "*=";
                default:
                    return "[Unknown Operation Name]";
            }
        }

        private void CheckForArithmeticOperations(Instruction instruction)
        {
            //Need 2 operand
            if (instruction.Operands.Length < 2) return;

            var arithmeticOpcodes = new[]
            {
                ud_mnemonic_code.UD_Imulss //Multiply Scalar Single 
            };

            //Needs to be a valid opcode
            if (!arithmeticOpcodes.Contains(instruction.Mnemonic)) return;

            //The first operand is guaranteed to be an XMM register
            var (name, type) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);

            //The second one COULD be a register, but it could also be a memory location containing a constant (say we're multiplying by 1.5, that'd be a constant)
            if (instruction.Operands[1].Type == ud_type.UD_OP_MEM && instruction.Operands[1].Base == ud_type.UD_R_RIP)
            {
                var virtualAddress = Utils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]) + _methodStart;
                _typeDump.Append($" ; - Arithmetic operation between {name} and constant stored at 0x{virtualAddress:X}");
                uint rawAddr = _cppAssembly.MapVirtualAddressToRaw(virtualAddress);
                var constantBytes = _cppAssembly.raw.SubArray((int) rawAddr, 4);
                var constantSingle = BitConverter.ToSingle(constantBytes, 0);
                _typeDump.Append($" - constant is bytes: {BitConverter.ToString(constantBytes)}, or value {constantSingle}");

                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(constantSingle).Append("\n");

                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) by {constantSingle} (System.Single) in-place\n");
            }
            else
            {
                _typeDump.Append($" ; Arithmetic operation between {name} and [unimplemented handler for operand type {instruction.Operands[1].Type}]");
            }
        }

        private void CheckForFieldWrites(Instruction instruction)
        {
            //Need 2 operands
            if (instruction.Operands.Length < 2) return;

            //First/destination has to be a memory offset
            if (instruction.Operands[0].Type != ud_type.UD_OP_MEM) return;

            var destinationField = GetFieldReferencedByOperand(instruction.Operands[0]);

            if (destinationField == null || instruction.Operands.Length <= 1 || instruction.Mnemonic == ud_mnemonic_code.UD_Icmp || instruction.Mnemonic == ud_mnemonic_code.UD_Itest) return;

            _typeDump.Append($" ; - Field Write into {destinationField}");

            var destReg = GetRegisterName(instruction.Operands[0]);
            _registerAliases.TryGetValue(destReg, out var destAlias);

            var (sourceAlias, sourceType) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);

            // if (instruction.Operands[1].Type == ud_type.UD_OP_REG)
            // {
            //     var sourceReg = GetRegisterName(instruction.Operands[1]);
            //     _registerAliases.TryGetValue(sourceReg, out sourceAlias);
            //
            //     _registerTypes.TryGetValue(sourceReg, out sourceType);
            // }
            // else if (instruction.Operands[1].Type == ud_type.UD_OP_MEM)
            // {
            //     //Memory operation - field copy?
            // }
            // else if (instruction.Operands[1].Type == ud_type.UD_OP_IMM)
            // {
            //     sourceAlias = $"{Utils.GetIMMValue(instruction, instruction.Operands[1])}";
            // }
            // else if (instruction.Operands[1].Type == ud_type.UD_OP_CONST)
            // {
            //     sourceAlias = instruction.Operands[1].LvalUDWord.ToString();
            // }

            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(destAlias).Append(".").Append(destinationField.Name).Append(" = ").Append(sourceAlias).Append("\n");
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Set field {destinationField.Name} (type {destinationField.FieldType.FullName}) of {destAlias} to {sourceAlias} (type {sourceType?.FullName})\n");
        }

        private void CheckForFieldReads(Instruction instruction)
        {
            //Pre-checks
            if (instruction.Operands.Length < 2 || instruction.Operands[1].Type != ud_type.UD_OP_MEM || instruction.Operands[1].Base == ud_type.UD_R_RIP || instruction.Operands[1].Base == ud_type.UD_R_RSP) return;

            //Check for field read
            var field = GetFieldReferencedByOperand(instruction.Operands[1]);

            var sourceReg = GetRegisterName(instruction.Operands[1]);
            _registerAliases.TryGetValue(sourceReg, out var sourceAlias);

            if (Utils.GetOperandMemoryOffset(instruction.Operands[1]) == 0)
            {
                //Just a regex assignment, but as a mem op. It happens.
                return;
            }

            if (field != null && instruction.Operands[0].Type == ud_type.UD_OP_REG)
            {
                _typeDump.Append($" ; - field read on {field} from type {field.DeclaringType.Name} in register {sourceReg}");

                //Compares don't create locals
                if (instruction.Mnemonic == ud_mnemonic_code.UD_Icmp || instruction.Mnemonic == ud_mnemonic_code.UD_Itest) return;

                var destReg = GetRegisterName(instruction.Operands[0]);

                _registerTypes.TryGetValue(sourceReg, out var sourceType);

                var readType = field.FieldType;

                _registerTypes[destReg] = readType;
                _registerAliases[destReg] = $"local{_localNum}";
                _registerContents[destReg] = field;

                if (field.IsStatic)
                {
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(field.FieldType.FullName).Append(" ").Append("local").Append(_localNum).Append(" = ").Append(field.DeclaringType.FullName).Append(".").Append(field.Name).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Reads static field {field} (type {readType}) and stores in new local variable local{_localNum} in reg {destReg}\n");
                }
                else
                {
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(field.FieldType.FullName).Append(" ").Append("local").Append(_localNum).Append(" = ").Append(sourceAlias).Append(".").Append(field.Name).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Reads field {field} (type {readType}) from {sourceAlias} (type {sourceType?.Name}) and stores in new local variable local{_localNum} in reg {destReg}\n");
                }

                _localNum++;
            }
            else if (field == null)
            {
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}WARN: Field Read: Failed to work out which field we are reading. Indicative of missed generated code or further calibration required, probably\n");
                _typeDump.Append($" ; - field read on unknown field from an unknown/unresolved type that's in reg {sourceReg}");
            }
            else
            {
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}WARN: Field Read: Don't know what we're doing with field {field}\n");
                _typeDump.Append(" ; - field read and unknown action");
            }
        }

        private void CheckForRegClear(Instruction instruction)
        {
            //Must be an xor or xorps
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Ixor && instruction.Mnemonic != ud_mnemonic_code.UD_Ixorps) return;

            //Must be dealing with 2 registers
            if (instruction.Operands[0].Type != ud_type.UD_OP_REG || instruction.Operands[1].Type != ud_type.UD_OP_REG) return;

            //And both must be the same
            if (instruction.Operands[0].Base != instruction.Operands[1].Base) return;

            var secondReg = GetRegisterName(instruction.Operands[1]);

            _typeDump.Append($" ; zero out register {secondReg}");

            if (_loopRegisters.Contains(secondReg)) return; //Slight Hack: Preserve types for suspected loop registers/counters.

            _registerAliases.Remove(secondReg);

            //Zeroed out, so literally set it to zero/int32
            if (instruction.Mnemonic == ud_mnemonic_code.UD_Ixorps)
            {
                _registerContents[secondReg] = 0.0f;
                _registerTypes[secondReg] = Utils.TryLookupTypeDefByName("System.Single").Item1;
            }
            else
            {
                _registerContents[secondReg] = 0;
                _registerTypes[secondReg] = Utils.TryLookupTypeDefByName("System.Int32").Item1; //TODO: Consider keeping some key types around for perf
            }
        }

        private void CheckForMoveIntoRegister(Instruction instruction)
        {
            //Checks

            //Need 2 operands
            if (instruction.Operands.Length < 2) return;

            //Destination must be a register
            if (instruction.Operands[0].Type != ud_type.UD_OP_REG) return;

            //Must be some sort of move
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Imov && instruction.Mnemonic != ud_mnemonic_code.UD_Imovaps && instruction.Mnemonic != ud_mnemonic_code.UD_Imovss
                && instruction.Mnemonic != ud_mnemonic_code.UD_Imovzx && instruction.Mnemonic != ud_mnemonic_code.UD_Ilea)
                return;

            var destReg = GetRegisterName(instruction.Operands[0]);

            //Ok now decide what to do based on what the second register is
            switch (instruction.Operands[1].Type)
            {
                case ud_type.UD_OP_REG:
                case ud_type.UD_OP_MEM when Utils.GetOperandMemoryOffset(instruction.Operands[1]) == 0:
                    //Simple case, reg => reg
                    var sourceReg = GetRegisterName(instruction.Operands[1]);
                    if (_registerAliases.ContainsKey(sourceReg))
                    {
                        _registerAliases[destReg] = _registerAliases[sourceReg];
                        if (_registerTypes.ContainsKey(sourceReg))
                        {
                            _registerTypes[destReg] = _registerTypes[sourceReg];
                            _typeDump.Append($" ; - {destReg} inherits {sourceReg}'s type {_registerTypes[sourceReg]}");
                        }
                    }
                    else if (_registerAliases.ContainsKey(destReg))
                    {
                        _registerAliases.Remove(destReg); //If we have one for the dest but not the source, clear the dest's alias.
                        _registerTypes.Remove(destReg); //And its type
                    }

                    break;
                case ud_type.UD_OP_MEM when instruction.Operands[1].Base != ud_type.UD_R_RIP:
                    //Don't need to handle this case as it's a field read to reg which is done elsewhere
                    return;
                case ud_type.UD_OP_MEM when instruction.Operands[1].Base == ud_type.UD_R_RIP:
                    //Reading either a global or a string literal into the register
                    var offset = Utils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]);
                    if (offset == 0) break;
                    var addr = _methodStart + offset;
                    _typeDump.Append($"; - Read on memory location 0x{addr:X}");
                    var glob = _globals.Find(g => g.Offset == addr); //TODO: Perf: make this a dictionary
                    if (glob.Offset == addr)
                    {
                        _typeDump.Append($" - this is global value {glob.Name} of type {glob.IdentifierType}");
                        _registerAliases[destReg] = $"global_{glob.IdentifierType}_{glob.Name}";
                        _registerContents[destReg] = glob;
                    }
                    else
                    {
                        //Try to read literal - used for native method name lookups
                        try
                        {
                            var actualAddress = _cppAssembly.MapVirtualAddressToRaw(addr);
                            _typeDump.Append(" - might be in file at " + actualAddress);
                            var c = Convert.ToChar(_cppAssembly.raw[actualAddress]);
                            if (char.IsLetter(c) && c < 'z')
                            {
                                var literal = new StringBuilder();
                                while (_cppAssembly.raw[actualAddress] != 0 && literal.Length < 250)
                                {
                                    literal.Append(Convert.ToChar(_cppAssembly.raw[actualAddress]));
                                    actualAddress++;
                                }

                                if (literal.Length > 4)
                                {
                                    _typeDump.Append(" - literal: " + literal);
                                    _registerAliases[destReg] = literal.ToString();
                                    _registerTypes[destReg] = Utils.TryLookupTypeDefByName("System.String").Item1;
                                    _registerContents.Remove(destReg);
                                    return;
                                }
                            }

                            //Clear register as we don't know what we're moving in
                            _registerAliases.Remove(destReg);
                            _registerTypes.Remove(destReg);
                            _registerContents.Remove(destReg);
                        }
                        catch (Exception)
                        {
                            //suppress
                        }
                    }

                    break;
                default:
                    return;
            }
        }

        private void CheckForCallRegister(Instruction instruction)
        {
            //Used after a native method has been looked up.

            //Checks
            //Must be some sort of call
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Ijmp && instruction.Mnemonic != ud_mnemonic_code.UD_Icall)
                return;

            //Must be a register we're calling
            if (instruction.Operands[0].Type != ud_type.UD_OP_REG) return;

            var register = GetRegisterName(instruction.Operands[0]);

            _typeDump.Append($" ; - jumps to contents of register {register}");

            //Test to see if we have the method ref (from a native lookup)
            _registerContents.TryGetValue(register, out var o);
            if (o != null && o is MethodDefinition method)
            {
                HandleFunctionCall(method, true);
            }
        }

        private void CheckForCallAddress(Instruction instruction)
        {
            //Check for a direct (NOT conditional) JMP or CALL to an address

            //Checks
            //Must be some sort of call
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Ijmp && instruction.Mnemonic != ud_mnemonic_code.UD_Icall)
                return;

            //Must NOT be a register we're calling
            if (instruction.Operands[0].Type == ud_type.UD_OP_REG) return;

            ulong jumpAddress;
            try
            {
                jumpAddress = Utils.GetJumpTarget(instruction, _methodStart + instruction.PC);
                _typeDump.Append($" ; jump to 0x{jumpAddress:X}");
            }
            catch (Exception)
            {
                _typeDump.Append(" ; Exception occurred locating target");
                return;
            }

            SharedState.MethodsByAddress.TryGetValue(jumpAddress, out var methodAtAddress);

            if (methodAtAddress != null)
            {
                HandleFunctionCall(methodAtAddress, true);
                return;
            }

            if (jumpAddress == _keyFunctionAddresses.AddrBailOutFunction)
            {
                _typeDump.Append(" - this is the bailout function and will be ignored.");
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrInitFunction)
            {
                _typeDump.Append(" - this is the initialization function and will be ignored.");
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrNewFunction)
            {
                _typeDump.Append(" - this is the constructor function.");
                var success = false;
                _registerAliases.TryGetValue("rcx", out var glob);
                if (glob != null)
                {
                    var match = Regex.Match(glob, "global_([A-Z]+)_([^/]+)");
                    if (match.Success)
                    {
                        Enum.TryParse<AssemblyBuilder.GlobalIdentifier.Type>(match.Groups[1].Value, out var type);
                        var global = _globals.Find(g => g.Name == match.Groups[2].Value && g.IdentifierType == type);
                        if (global.Offset != 0)
                        {
                            var (definedType, genericParams) = Utils.TryLookupTypeDefByName(global.Name);

                            if (definedType != null)
                            {
                                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(definedType.FullName).Append(" ").Append("local").Append(_localNum).Append(" = new ").Append(definedType.FullName).Append("()\n");
                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an instance of type {definedType.FullName}{(genericParams.Length > 0 ? $" with generic parameters {string.Join(",", genericParams)}" : "")}\n");
                                PushMethodReturnTypeToLocal(definedType);
                                success = true;
                            }
                            else
                            {
                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an instance of (unresolved) type {global.Name}\n");
                                success = true;
                            }
                        }
                    }
                }

                if (!success)
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an instance of [something]\n");
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrInitStaticFunction)
            {
                _typeDump.Append(" - this is the static class initializer and will be ignored");
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrNativeLookup)
            {
                _typeDump.Append(" - this is the native lookup function");
                _registerAliases.TryGetValue("rcx", out var functionName);

                //Native methods usually have an IL counterpart - but that just calls this method with its own name. Even so, we can point at that, for now.
                if (functionName == null) return;

                //Should be a FQ function name, but with the type and function separated with a ::, cpp style.
                var split = functionName.Split(new[] {"::"}, StringSplitOptions.None);

                var typeName = split[0];
                var (type, _) = Utils.TryLookupTypeDefByName(typeName);

                var methodName = split[1];
                methodName = methodName.Substring(0, methodName.IndexOf("(", StringComparison.Ordinal));

                MethodDefinition mDef = null;
                if (type != null)
                    mDef = type.Methods.First(mtd => mtd.Name.EndsWith(methodName));

                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Looks up native function by name {functionName} => {mDef?.FullName}\n");
                if (mDef == null) return;

                _registerAliases["rax"] = $"{type.FullName}.{mDef.Name}";
                _registerContents["rax"] = mDef;
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrNativeLookupGenMissingMethod)
            {
                _typeDump.Append(" - this is the native lookup bailout function");
            }
            else
            {
                //Is this somewhere in this function?
                if (_methodStart <= jumpAddress && jumpAddress <= _methodEnd)
                {
                    var pos = jumpAddress - _methodStart;
                    _typeDump.Append($" - offset 0x{pos:X} in this function");

                    //Could be an else jump
                    if (_blockDepth <= 0) return;

                    //We're in an if, so it's likely this is to jump over the else
                    //Clear the most recent block (though it probably has just expired anyway), this is the end of the if
                    _indentCounts.RemoveAt(_indentCounts.Count - 1);

                    //Now we need to find the ELSE length
                    //This current jump goes to the first instruction after it, so take its address - our PC to find function length
                    //TODO: This is ok, but still needs work in E.g AudioDriver_Resume
                    var instructionIdx = _instructions.FindIndex(i => i.PC == pos);
                    if (instructionIdx < 0) return;
                    instructionIdx++;
                    var numToIndent = instructionIdx - _instructions.IndexOf(instruction);
                    _indentCounts.Add(numToIndent);
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 1)}Else:\n"); //This is a +1 not a +2 to remove the indent caused by the current if block
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth - 1)).Append("else:\n");
                }
                else if (instruction.Mnemonic == ud_mnemonic_code.UD_Icall)
                {
                    //Heuristic analysis: Sometimes we call a function by calling its superclass (which isn't defined anywhere so comes up as an undefined function call)
                    //But we pass in the method reference of the function as an extra param (one more than can actually be used)
                    //Therefore, work backwards through the argument registers looking for a function ref as we're at an unknown function by now
                    var paramRegisters = new List<string>(new[] {"rcx/xmm0", "rdx/xmm1", "r8/xmm2", "r9/xmm3"});
                    paramRegisters.Reverse();

                    var providedParamCount = 4;
                    foreach (var possibility in paramRegisters)
                    {
                        providedParamCount -= 1;
                        foreach (var register in possibility.Split('/'))
                        {
                            _registerAliases.TryGetValue(register, out var alias);
                            if (alias == null || !alias.StartsWith("global_METHOD")) continue;

                            //Success! Now we want the method ref
                            var methodFullName = alias.Replace("global_METHOD_", "").Replace("_" + register, "");
                            var split = methodFullName.Split(new[] {'.'}, 999, StringSplitOptions.None).ToList();
                            var methodName = split.Last();
                            if (methodName.Contains("("))
                                methodName = methodName.Substring(0, methodName.IndexOf("(", StringComparison.Ordinal));
                            split.RemoveAt(split.Count - 1);
                            var typeName = string.Join(".", split);


                            if (typeName.Count(c => c == '<') > 1) //Clean up double type param
                                if (Regex.Match(typeName, "([^#]+)<\\w+>(<[^#]+>)") is { } match && match != null && match.Success)
                                    typeName = match.Groups[1].Value + match.Groups[2].Value;

                            var (definedType, genericParams) = Utils.TryLookupTypeDefByName(typeName);

                            var genericTypes = genericParams.Select(Utils.TryLookupTypeDefByName).Select(t => (TypeReference) t.Item1).ToList();

                            var method = definedType?.Methods?.FirstOrDefault(methd => methd.Name.Split('.').Last() == methodName);

                            if (method == null) continue;

                            var requiredCount = (method.IsStatic ? 0 : 1) + method.Parameters.Count;

                            if (requiredCount != providedParamCount) continue;

                            var returnType = method.ReturnType;

                            if (returnType.Name == "Object" && genericTypes.Count == 1 && genericTypes.All(t => t != null))
                                returnType = genericTypes.First();

                            HandleFunctionCall(method, true, returnType);
                        }
                    }
                }
            }
        }

        private void CheckForConditions(Instruction instruction)
        {
            //Checks
            //Need 2 operands
            if (instruction.Operands.Length < 2) return;

            //Needs to be TEST or CMP
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Icmp && instruction.Mnemonic != ud_mnemonic_code.UD_Itest && instruction.Mnemonic != ud_mnemonic_code.UD_Iucomiss && instruction.Mnemonic != ud_mnemonic_code.UD_Icomiss) return;

            _lastComparison = new Tuple<(string, TypeReference), (string, TypeReference)>(GetDetailsOfReferencedObject(instruction.Operands[0], instruction), GetDetailsOfReferencedObject(instruction.Operands[1], instruction));

            _typeDump.Append($" ; - Comparison between {_lastComparison.Item1.Item1} and {_lastComparison.Item2.Item1}");
        }

        private void CheckForConditionalJumps(Instruction instruction)
        {
            //Checks
            //We need an operand
            if (instruction.Operands.Length < 1) return;

            //Needs to be a conditional jump
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Ijz && instruction.Mnemonic != ud_mnemonic_code.UD_Ijnz && instruction.Mnemonic != ud_mnemonic_code.UD_Ijge && instruction.Mnemonic != ud_mnemonic_code.UD_Ijle && instruction.Mnemonic != ud_mnemonic_code.UD_Ijg &&
                instruction.Mnemonic != ud_mnemonic_code.UD_Ijl && instruction.Mnemonic != ud_mnemonic_code.UD_Ija) return;

            if (_lastComparison.Item1.Item1 == "")
            {
                _typeDump.Append(" ; - WARN Comparison Jump without comparison statement?");
                return;
            }

            var (comparisonItemA, typeA) = _lastComparison.Item1;
            var (comparisonItemB, typeB) = _lastComparison.Item2;

            //TODO: Clear out crap [unknown global] if statements

            var dest = Utils.GetJumpTarget(instruction, _methodStart + instruction.PC);

            //Going back to an earlier point in the function - loop.
            //This works because the compiler puts all if statements *after* the main function body for jumping forward to, then back.
            //Still need checks in those jump back cases.
            var isLoop = dest < instruction.PC + _methodStart && dest > _methodStart;

            // _lastComparison = new Tuple<object, object>("", ""); //Clear last comparison
            string condition;
            switch (instruction.Mnemonic)
            {
                case ud_mnemonic_code.UD_Ijz:
                {
                    var isSelfCheck = Equals(comparisonItemA, comparisonItemB);
                    var isBoolean = typeA?.Name == "Boolean";
                    condition = isSelfCheck ? $"{comparisonItemA} is {(isBoolean ? "false" : "zero or null")}" : $"{comparisonItemA} == {comparisonItemB}";
                    break;
                }
                case ud_mnemonic_code.UD_Ijnz:
                {
                    var isSelfCheck = Equals(comparisonItemA, comparisonItemB);
                    var isBoolean = typeA?.Name == "Boolean";
                    condition = isSelfCheck ? $"{comparisonItemA} is {(isBoolean ? "true" : "NOT zero or null")}" : $"{comparisonItemA} != {comparisonItemB}";
                    break;
                }
                case ud_mnemonic_code.UD_Ijge:
                    condition = $"{comparisonItemA} >= {comparisonItemB}";
                    break;
                case ud_mnemonic_code.UD_Ijle:
                    condition = $"{comparisonItemA} <= {comparisonItemB}";
                    break;
                case ud_mnemonic_code.UD_Ijg:
                case ud_mnemonic_code.UD_Ijp:
                    condition = $"{comparisonItemA} > {comparisonItemB}";
                    break;
                case ud_mnemonic_code.UD_Ijl:
                    condition = $"{comparisonItemA} < {comparisonItemB}";
                    break;
                case ud_mnemonic_code.UD_Ija:
                    condition = $"unsigned({comparisonItemA} > {comparisonItemB})";
                    break;
                default:
                    condition = null;
                    break;
            }

            if (condition == null) return;

            if (isLoop)
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 1)}Code from 0x{dest:X} until 0x{instruction.PC + _methodStart:X} repeats while {condition}\n");
            else
            {
                //If statement
                if (dest >= _methodEnd)
                {
                    //TODO: We REALLY need a better way to a) identify EOF so jumps are treated as ifs when they should be and b) not follow stupid recursive jumps.
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}If {condition}, then:\n");
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append("if (").Append(condition).Append("):\n");
                    // jumpTable.Add(dest, new List<string>());

                    //This is the biggest pain in the butt. We can't realistically decompile this later as we need the current register states etc.
                    //So we have to do it now.
                    var currentOffset = _cppAssembly.MapVirtualAddressToRaw(dest);
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 3)}[If Body at 0x{currentOffset:X}]\n");
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth + 1)).Append("[undeciphered]\n");
                }
                else
                {
                    if (dest < _methodEnd && dest > _methodStart)
                    {
                        //Jump within function - invert condition and indent for a certain number of lines
                        var offset = dest - _methodStart;
                        var instructionIdx = _instructions.FindIndex(i => i.PC == offset);
                        if (instructionIdx >= 0)
                        {
                            instructionIdx++;
                            var numToIndent = instructionIdx - _instructions.IndexOf(instruction);
                            _indentCounts.Add(numToIndent);
                            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}If {Utils.InvertCondition(condition)}:\n");
                            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append("if (").Append(Utils.InvertCondition(condition)).Append("):\n");
                            // var targetInstruction = _instructions[instructionIdx];
                            // lock (Disassembler.Translator)
                            // {
                            //     Disassembler.Translator.IncludeAddress = false;
                            //     Disassembler.Translator.IncludeBinary = false;
                            //     _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Jumps to instruction #{instructionIdx + 1} in this function ({targetInstruction}) if {condition}\n");
                            //     Disassembler.Translator.IncludeAddress = true;
                            //     Disassembler.Translator.IncludeBinary = true;
                            // }

                            return;
                        }
                    }

                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Jumps to 0x{dest:X} if {condition}\n");
                }
            }
        }

        private void CheckForIncrements(Instruction instruction)
        {
            //Checks
            //Need an operand
            if (instruction.Operands.Length < 1) return;

            //Needs to be an INC
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Iinc) return;

            var register = GetRegisterName(instruction.Operands[0]);
            _registerAliases.TryGetValue(register, out var alias);
            if (alias == null)
                alias = $"the value in register {register}";

            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(alias).Append("++\n");
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Increases {alias} by one.\n");
        }

        private void CheckForReturn(Instruction instruction)
        {
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Iret && instruction.Mnemonic != ud_mnemonic_code.UD_Iretf) return;

            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Returns\n");
            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append("return\n");
        }

        private AssemblyBuilder.GlobalIdentifier? GetGlobalInReg(string reg)
        {
            _registerAliases.TryGetValue(reg, out var glob);
            if (glob != null)
            {
                var match = Regex.Match(glob, "global_([A-Z]+)_([^/]+)");
                if (match.Success)
                {
                    Enum.TryParse<AssemblyBuilder.GlobalIdentifier.Type>(match.Groups[1].Value, out var type);
                    var global = _globals.Find(g => g.Name == match.Groups[2].Value && g.IdentifierType == type);
                    if (global.Offset != 0)
                    {
                        return global;
                    }
                }
            }

            return null;
        }
    }
}