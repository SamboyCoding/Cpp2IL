using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly TypeDefinition TypeReference = Utils.TryLookupTypeDefByName("System.Type").Item1;
        private static readonly TypeDefinition StringReference = Utils.TryLookupTypeDefByName("System.String").Item1;
        private static readonly TypeDefinition BooleanReference = Utils.TryLookupTypeDefByName("System.Boolean").Item1;
        private static readonly TypeDefinition FloatReference = Utils.TryLookupTypeDefByName("System.Single").Item1;
        private static readonly TypeDefinition ByteReference = Utils.TryLookupTypeDefByName("System.Byte").Item1;
        private static readonly TypeDefinition ShortReference = Utils.TryLookupTypeDefByName("System.Int16").Item1;
        private static readonly TypeDefinition IntegerReference = Utils.TryLookupTypeDefByName("System.Int32").Item1;
        private static readonly TypeDefinition LongReference = Utils.TryLookupTypeDefByName("System.Int64").Item1;
        private static readonly TypeDefinition ArrayReference = Utils.TryLookupTypeDefByName("System.Array").Item1;

        private static readonly ConcurrentDictionary<ud_type, string> CachedRegNames = new ConcurrentDictionary<ud_type, string>();
        private static readonly ConcurrentDictionary<ulong, TypeDefinition> exceptionThrowerAddresses = new ConcurrentDictionary<ulong, TypeDefinition>();

        private readonly MethodDefinition _methodDefinition;
        private readonly ulong _methodStart;
        private ulong _methodEnd;
        private readonly KeyFunctionAddresses _keyFunctionAddresses;
        private readonly PE.PE _cppAssembly;
        private ConcurrentDictionary<string, string> _registerAliases;
        private ConcurrentDictionary<string, TypeDefinition> _registerTypes;
        private ConcurrentDictionary<string, object> _registerContents;
        private StringBuilder _methodFunctionality;
        private StringBuilder _psuedoCode = new StringBuilder();
        private StringBuilder _typeDump;
        private List<Instruction> _instructions;
        private int _blockDepth;
        private int _localNum;
        private List<string> _loopRegisters;

        private Tuple<(string, TypeDefinition, object), (string, TypeDefinition, object)> _lastComparison;
        private List<int> _indentCounts = new List<int>();
        private Stack<PreBlockCache> _savedRegisterStates = new Stack<PreBlockCache>();

        private Dictionary<int, string> _stackAliases = new Dictionary<int, string>();
        private Dictionary<int, TypeDefinition> _stackTypes = new Dictionary<int, TypeDefinition>();

        private TaintReason _taintReason = TaintReason.UNTAINTED;

        private BlockType _currentBlockType = BlockType.NONE;

        private readonly List<ulong> unknownMethodAddresses = new List<ulong>();

        private readonly ud_mnemonic_code[] _inlineArithmeticOpcodes =
        {
            ud_mnemonic_code.UD_Imul, //Multiply
            ud_mnemonic_code.UD_Iimul, //Signed multiply
            ud_mnemonic_code.UD_Imulss, //Multiply Scalar Single
            ud_mnemonic_code.UD_Isubss, //Subtract Scalar Single
            ud_mnemonic_code.UD_Isub, //Subtract
            ud_mnemonic_code.UD_Iadd, //Add
        };

        private readonly ud_mnemonic_code[] _localCreatingArithmeticOpcodes =
        {
            ud_mnemonic_code.UD_Isar, //Shift, Arithmetic, Right
            ud_mnemonic_code.UD_Ishr, //Shift right (logical)
        };

        private readonly ud_mnemonic_code[] _moveOpcodes =
        {
            ud_mnemonic_code.UD_Imov, //General move
            ud_mnemonic_code.UD_Imovaps,
            ud_mnemonic_code.UD_Imovss, //Move scalar single
            ud_mnemonic_code.UD_Imovsd, //Move scalar double
            ud_mnemonic_code.UD_Imovzx, //Move negated
            ud_mnemonic_code.UD_Ilea, //Load effective address
            ud_mnemonic_code.UD_Imovups
        };

        internal AsmDumper(MethodDefinition methodDefinition, CppMethodData method, ulong methodStart, KeyFunctionAddresses keyFunctionAddresses, PE.PE cppAssembly)
        {
            _methodDefinition = methodDefinition;
            _methodStart = methodStart;

            _keyFunctionAddresses = keyFunctionAddresses;
            _cppAssembly = cppAssembly;

            //Pass 0: Disassemble
            _instructions = Utils.DisassembleBytes(method.MethodBytes);
        }

        private void TaintMethod(TaintReason reason)
        {
            if (_taintReason != TaintReason.UNTAINTED) return;

            _taintReason = reason;
            _typeDump.Append($" ; !!! METHOD TAINTED HERE: {reason} (COMPLEXITY {(int) reason}) !!!");
            _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}//!!!METHOD TAINTED HERE: {reason} (COMPLEXITY {(int) reason})!!!\n");
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}!!! METHOD TAINTED HERE: {reason} (COMPLEXITY {(int) reason}) !!!\n");
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
                    //Skip however many instructions
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

        internal TaintReason AnalyzeMethod(StringBuilder typeDump, ref List<ud_mnemonic_code> allUsedMnemonics)
        {
            _typeDump = typeDump;
            _registerAliases = new ConcurrentDictionary<string, string>();
            _registerTypes = new ConcurrentDictionary<string, TypeDefinition>();
            _registerContents = new ConcurrentDictionary<string, object>();

            //Map of jumped-to addresses to functionality summaries (for if statements)
            var jumpTable = new Dictionary<ulong, List<string>>();

            //As we're on windows, function params are passed RCX RDX R8 R9, then the stack
            //If these are floating point numbers, they're put in XMM0 to 3
            //Register eax/rax/whatever you want to call it is the return value (both of any functions called in this one and this function itself)

            typeDump.Append($"Method: {_methodDefinition.FullName}:");

            _methodFunctionality = new StringBuilder();

            //Pass 1: Removal of unneeded generated code
            _instructions = TrimOutIl2CppCrap(_instructions);

            //Prevent overrunning into another function
            var lastInstructionInThisMethod = _instructions.FindIndex(i => SharedState.MethodsByAddress.ContainsKey(i.PC + _methodStart));

            if (lastInstructionInThisMethod > 0)
            {
                _instructions = _instructions.Take(lastInstructionInThisMethod + 2).ToList();
            }

            //Int3 is padding, we stop at the first one.
            var idx = _instructions.FindIndex(i => i.Mnemonic == ud_mnemonic_code.UD_Iint3);
            if (idx > 0 && idx < _instructions.Count - 2 && _instructions[idx + 2].Mnemonic == ud_mnemonic_code.UD_Iint3)
                _instructions = _instructions.Take(idx + 2).ToList();

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
                _registerTypes[loopRegister] = IntegerReference;
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
                        _registerTypes[reg] = Utils.TryLookupTypeDefByName(parameter.ParameterType.FullName).Item1;
                        if (parameter.ParameterType.IsArray)
                        {
                            _registerTypes[reg] = parameter.ParameterType.GetElementType().Resolve();
                            _registerContents[reg] = new ArrayData(int.MaxValue, _registerTypes[reg]);
                        }
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
            _lastComparison = new Tuple<(string, TypeDefinition, object), (string, TypeDefinition, object)>(("", null, null), ("", null, null));
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

                if (instruction.Mnemonic == ud_mnemonic_code.UD_Iret && _blockDepth == 0)
                    break; //Can't continue

                var old = _indentCounts.Count;

                _indentCounts = _indentCounts
                    .Select(i => i - 1)
                    .Where(i => i > 0)
                    .ToList();

                if (_indentCounts.Count < old)
                {
                    var count = old - _indentCounts.Count;

                    while (count > 0)
                    {
                        PopBlock();
                        count--;
                    }
                }
            }

            typeDump.Append($"\n\tMethod Synopsis:\n{_methodFunctionality}\n\n");

            typeDump.Append($"\n\tGenerated Pseudocode:\n\n{_psuedoCode}\n");

            return _taintReason;
        }

        private void PushBlock(int toAdd, BlockType type)
        {
            _indentCounts.Add(toAdd);

            _savedRegisterStates.Push(new PreBlockCache(_registerAliases, _registerContents, _registerTypes, _currentBlockType));

            _currentBlockType = type;

            _blockDepth++;
        }

        private void PopBlock()
        {
            //Pop cached state
            var savedRegisterState = _savedRegisterStates.Pop();
            _registerAliases = savedRegisterState.Aliases;
            _registerContents = savedRegisterState.Constants;
            _registerTypes = savedRegisterState.Types;

            _currentBlockType = savedRegisterState.BlockType;

            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth - 1)).Append("}").Append("\n");
            _blockDepth--;
        }

        private void PerformInstructionChecks(Instruction instruction)
        {
            //Detect field writes
            CheckForFieldWrites(instruction);

            //And check for reads on (non-global) fields.
            CheckForFieldArrayAndStackReads(instruction);

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

            //Check for boolean in-place invert
            CheckForBooleanInvert(instruction);

            //Check for push and pop operations which shift the stack
            CheckForPushPop(instruction);

            //Check for RET
            CheckForReturn(instruction);
        }

        private void HandleFunctionCall(MethodDefinition target, bool processReturnType, Instruction instruction, TypeDefinition returnType = null)
        {
            if (returnType == null)
                returnType = Utils.TryLookupTypeDefByName(target.ReturnType.FullName).Item1;

            _typeDump.Append($" - function {target.FullName}");
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Calls {(target.IsStatic ? "static" : "instance")} function {target.FullName}");
            var args = new List<string>();

            string methodName = null;

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

                    if (_registerAliases[possibility] == "this" && target.DeclaringType.FullName != _methodDefinition.DeclaringType.FullName)
                    {
                        //Supercall
                        methodName = $"base.{target.Name}";
                    }
                    else
                    {
                        methodName = $"{_registerAliases[possibility]}.{target.Name}";
                    }

                    break;
                }
            }
            else
                methodName = $"{target.DeclaringType.FullName}.{target.Name}";

            var paramNames = new List<string>();

            foreach (var parameter in target.Parameters)
            {
                var possibilities = paramRegisters.First().Split('/');
                paramRegisters.RemoveAt(0);
                var success = false;
                foreach (var possibility in possibilities)
                {
                    //Could be a numerical value, check
                    if ((parameter.ParameterType.IsPrimitive || parameter.ParameterType.Resolve()?.IsEnum == true) && _registerContents.ContainsKey(possibility) && _registerContents[possibility]?.GetType().IsPrimitive == true)
                    {
                        //Coerce if bool
                        if (parameter.ParameterType.Name == "Boolean")
                        {
                            args.Add($"{Convert.ToInt64(_registerContents[possibility]) != 0} (coerced to bool from {_registerContents[possibility]}) (type CONSTANT) as {parameter.Name} in register {possibility}");
                            paramNames.Add((Convert.ToInt64(_registerContents[possibility]) != 0).ToString());
                        }
                        else if (parameter.ParameterType.Resolve() is {} def && def.IsEnum)
                        {
                            var second = _registerContents[possibility];
                            var targetValue = def.Fields
                                .Where(f => f.Name != "value__")
                                .FirstOrDefault(f =>
                                {
                                    try
                                    {
                                        return (int) f.Constant == (int) second;
                                    }
                                    catch (Exception)
                                    {
                                        return f.Constant == second;
                                    }
                                });

                            if (targetValue != null)
                            {
                                args.Add($"{targetValue.Name} (value from enum {def.FullName}) as {parameter.Name} in register {possibility}");
                                paramNames.Add($"{def.FullName}.{targetValue.Name}");
                                _registerTypes[possibility] = def;
                            }
                            else
                                continue;
                        }
                        else
                        {
                            args.Add($"{_registerContents[possibility]} (type CONSTANT) as {parameter.Name} in register {possibility}");
                            paramNames.Add(_registerContents[possibility]?.ToString());
                        }

                        success = true;
                        break;
                    }

                    //Check for null as a literal
                    if (!parameter.ParameterType.IsPrimitive && _registerContents.ContainsKey(possibility) && (_registerContents[possibility] as int?) is {} val && val == 0)
                    {
                        args.Add($"NULL (as a literal) as {parameter.Name} in register {possibility}");
                        paramNames.Add("null");


                        success = true;
                        break;
                    }

                    if (!_registerAliases.ContainsKey(possibility) || _registerAliases[possibility] == null)
                    {
                        continue;
                    }

                    if (_registerAliases[possibility].StartsWith("global_LITERAL"))
                    {
                        var global = GetGlobalInReg(possibility);
                        if (global.HasValue)
                        {
                            args.Add($"'{global.Value.Name}' (LITERAL type System.String) as {parameter.Name} in register {possibility}");
                            paramNames.Add($"'{global.Value.Name}'");

                            if (!parameter.ParameterType.IsAssignableFrom(StringReference))
                            {
                                TaintMethod(TaintReason.METHOD_PARAM_MISMATCH);
                                _typeDump.Append($" ; - parameter {parameter.Name} expects a {parameter.ParameterType.FullName} but got a string (literal)");
                            }

                            success = true;
                            break;
                        }
                    }

                    _registerAliases.TryGetValue(possibility, out var alias);
                    _registerTypes.TryGetValue(possibility, out var type);

                    if (_registerContents.ContainsKey(possibility) && _registerContents[possibility] is StackPointer sPtr)
                    {
                        if (!_stackAliases.ContainsKey(sPtr.Address))
                            continue; //Try next register - we don't have a value here.

                        alias = _stackAliases.ContainsKey(sPtr.Address) ? _stackAliases[sPtr.Address] : $"[unknown value in stack at offset 0x{sPtr.Address:X}]";
                        type = _stackTypes.ContainsKey(sPtr.Address) ? _stackTypes[sPtr.Address] : LongReference;
                    }

                    args.Add($"{alias} (type {type?.Name}) as {parameter.Name} in register {possibility}");
                    paramNames.Add(alias);
                    success = true;

                    //TODO: This isn't working properly with interfaces - e.g. Control_GlobalTime extends MonoBehavior which implements UnityEngine.Object, but this returns false
                    if (type == null || !parameter.ParameterType.IsAssignableFrom(type))
                    {
                        if (parameter.ParameterType.IsArray && _registerContents.TryGetValue(possibility, out var cons))
                        {
                            if (cons is ArrayData arrayData && parameter.ParameterType.GetElementType().FullName == arrayData.ElementType.FullName)
                            {
                                goto typematch; //I apologise.
                            }
                        }

                        //TODO: This is a genuine issue with floats - if the param is a floating point type we should prefer xmm registers over standard. cause you know that's how it works.
                        TaintMethod(TaintReason.METHOD_PARAM_MISMATCH);
                        _typeDump.Append(" ; - mismatched param - type " + (type?.FullName ?? "null") + $" is not assignable from {parameter.ParameterType.FullName}");
                    }

                    break;
                }

                typematch:
                if (!success)
                {
                    TaintMethod(TaintReason.METHOD_PARAM_MISSING);
                    args.Add($"<unknown> as {parameter.Name} in one of the registers {string.Join("/", possibilities)}");
                    paramNames.Add("<unknown>");
                }

                if (paramRegisters.Count != 0) continue;

                TaintMethod(TaintReason.METHOD_PARAM_MISSING);
                args.Add(" ... and more, out of space in registers.");
                break;
            } //End for parameter in parameters

            if (target.HasGenericParameters && paramRegisters.Count > 0)
            {
                _typeDump.Append(" ; - method should be generic");
                var possibilities = paramRegisters.First().Split('/');
                paramRegisters.RemoveAt(0);
                foreach (var possibility in possibilities)
                {
                    _registerContents.TryGetValue(possibility, out var potentialGlob);
                    if (potentialGlob is AssemblyBuilder.GlobalIdentifier g && g.Offset != 0 && g.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.METHOD)
                    {
                        _typeDump.Append($" ; - generic method def located, is {g.Name}");
                        var genericParams = g.Name.Substring(g.Name.LastIndexOf("<", StringComparison.Ordinal) + 1);
                        genericParams = genericParams.Remove(genericParams.Length - 1);

                        var genericCount = genericParams.Split(',').Length;

                        if (genericCount == 1)
                            returnType = Utils.TryLookupTypeDefByName(genericParams).Item1;

                        methodName += "<" + genericParams + ">";
                        _methodFunctionality.Append(" with generic params ").Append(genericParams);
                    }
                    else
                    {
                        TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                    }
                }
            }

            if (processReturnType && returnType != null && returnType.Name != "Void")
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(returnType.FullName).Append(" ").Append($"local{_localNum} = ");
            else
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth));

            _psuedoCode.Append(methodName);

            _psuedoCode.Append("(").Append(string.Join(", ", paramNames)).Append(")\n");

            if (args.Count > 0)
                _methodFunctionality.Append($" with parameters: {string.Join(", ", args)}");
            _methodFunctionality.Append("\n");

            if (processReturnType && returnType != null && returnType.Name != "Void")
            {
                PushMethodReturnTypeToLocal(returnType);
                if (target.ReturnType?.Name?.EndsWith("[]") == true) //Goddamit cecil why don't array types have typedefinition things. 
                    _registerContents["rax"] = new ArrayData(0UL, returnType);
            }
            else if (returnType == null)
            {
                _typeDump.Append($"; - Could not resolve method return type (is {target.ReturnType} / {target.MethodReturnType}");
                TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
            }

            if (instruction.Mnemonic == ud_mnemonic_code.UD_Ijmp)
                //Jmp = not coming back to this function
                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}return\n");
        }

        private string PushMethodReturnTypeToLocal(TypeDefinition returnType)
        {
            //Floating point => xmm0
            //Boolean => al
            var reg = returnType.Name == "Single" || returnType.Name == "Double" || returnType.Name == "Decimal"
                ? "xmm0"
                : "rax";
            _registerTypes[reg] = returnType;
            _registerAliases[reg] = $"local{_localNum}";
            if (_registerContents.TryRemove(reg, out _))
            {
                _typeDump.Append($" ; - {reg} loses its constant value here due to the return value.");
            }

            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates local variable local{_localNum} of type {returnType.Name} in {reg} and sets it to the return value\n");
            _localNum++;

            return reg;
        }

        private string GetRegisterName(Operand operand)
        {
            var theBase = operand.Base;
            if (!CachedRegNames.TryGetValue(theBase, out var ret))
            {
                ret = UpscaleRegisters(theBase.ToString().Replace("UD_R_", "").ToLower());
                CachedRegNames[theBase] = ret;
            }

            return ret;
        }

        private string UpscaleRegisters(string replaceIn)
        {
            if (replaceIn.Length < 2) return replaceIn;

            //Special case the few 8-bit register: "al" => "rax" etc
            if (replaceIn == "al")
                return "rax";
            if (replaceIn == "bl")
                return "rbx";
            if (replaceIn == "dl")
                return "rdx";
            if (replaceIn == "ax")
                return "rax";
            if (replaceIn == "cx" || replaceIn == "cl")
                return "rcx";

            //R9d, etc.
            if (replaceIn[0] == 'r' && replaceIn[replaceIn.Length - 1] == 'd')
                return replaceIn.Substring(0, replaceIn.Length - 1);

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

        private (string, TypeDefinition?, object) GetDetailsOfReferencedObject(Operand operand, Instruction i)
        {
            var sourceReg = GetRegisterName(operand);
            string objectName = null;
            TypeDefinition objectType = null;
            object constant = null;
            switch (operand.Type)
            {
                case ud_type.UD_OP_MEM:
                    //Check array read
                    if (_registerContents.ContainsKey(sourceReg) && (_registerTypes.ContainsKey(sourceReg) && _registerTypes[sourceReg]?.IsArray == true || _registerAliases.ContainsKey(sourceReg) && _registerContents[sourceReg] is ArrayData))
                    {
                        _typeDump.Append($" ; - probably an array read as we have an aliased arraydata in reg {sourceReg}");
                        var offset = Utils.GetOperandMemoryOffset(operand);

                        _typeDump.Append($" ; offset is 0x{offset:X}");

                        if (offset == 0x18)
                        {
                            //Array length
                            objectName = $"{_registerAliases[sourceReg]}.Length";
                            objectType = IntegerReference;
                            break;
                        }

                        var index = (offset - 0x20) / 8;

                        _typeDump.Append($" ; index is {index}");

                        if (index >= 0)
                        {
                            objectName = $"{_registerAliases[sourceReg]}[{index}]";
                            if (_registerContents.ContainsKey(sourceReg))
                                objectType = ((ArrayData) _registerContents[sourceReg]).ElementType;
                            else
                                objectType = _registerTypes[sourceReg].GetElementType().Resolve();

                            _typeDump.Append($" ; name is {objectName}");

                            break;
                        }
                    }

                    //Field read
                    if (GetFieldReferencedByOperand(operand) is { } field)
                    {
                        _registerAliases.TryGetValue(sourceReg, out var fieldReadAlias);
                        if (fieldReadAlias == null)
                            fieldReadAlias = $"the value in register {sourceReg}";
                        objectName = $"{fieldReadAlias}.{field.Name}";
                        objectType = Utils.TryLookupTypeDefByName(field.FieldType.FullName).Item1;
                        break;
                    }

                    //Check for global
                    if (operand.Base == ud_type.UD_R_RIP)
                    {
                        var globalAddr = Utils.GetOffsetFromMemoryAccess(i, operand) + _methodStart;
                        if (SharedState.GlobalsDict.TryGetValue(globalAddr, out var glob))
                        {
                            if (glob.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.LITERAL)
                            {
                                objectName = $"\"{glob.Name}\"";
                                objectType = StringReference;
                                constant = glob.Name;
                            }
                            else
                            {
                                objectName = $"global_{glob.IdentifierType}_{glob.Name}";
                                objectType = LongReference;
                                constant = glob;
                            }
                        }
                        else
                            objectName = $"[unknown global variable at 0x{globalAddr:X}]";
                    }

                    break;
                case ud_type.UD_OP_REG:
                    _registerAliases.TryGetValue(sourceReg, out var alias);
                    _registerContents.TryGetValue(sourceReg, out constant);
                    _registerTypes.TryGetValue(sourceReg, out objectType);

                    if (alias?.StartsWith("global_") != false && constant is AssemblyBuilder.GlobalIdentifier glob2)
                    {
                        if (glob2.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.LITERAL)
                        {
                            objectType = StringReference;
                            objectName = $"'{glob2.Name}'";
                            constant = glob2.Name;
                            break;
                        }
                    }

                    objectType ??= Utils.TryLookupTypeDefByName(constant?.GetType().FullName).Item1;
                    objectName = alias ??
                                 (constant?.GetType().IsPrimitive == true
                                     ? constant.ToString()
                                     : $"[value in {sourceReg}]");
                    break;
                case ud_type.UD_OP_CONST:
                    if (operand.LvalUDWord == 0)
                        objectName = "0 / null";
                    else
                        objectName = $"0x{operand.LvalUDWord:X}";
                    objectType = LongReference;
                    constant = operand.LvalUDWord;
                    break;
                case ud_type.UD_OP_IMM:
                    var imm = Utils.GetImmediateValue(i, operand);
                    if (imm == 0)
                        objectName = "0 / null";
                    else
                        objectName = $"0x{imm:X}";
                    objectType = LongReference;
                    constant = Utils.GetImmediateValue(i, operand);
                    break;
            }

            return (objectName ?? $"<unknown refobject type = {operand.Type} constantval = {operand.LvalUDWord} base = {operand.Base}>", objectType, constant);
        }

        private FieldDefinition GetFieldReferencedByOperand(Operand operand)
        {
            if (operand.Type != ud_type.UD_OP_MEM || operand.Base == ud_type.UD_R_RIP) return null;

            var sourceReg = GetRegisterName(operand);
            var offset = Utils.GetOperandMemoryOffset(operand);

            if (offset == 0 && _registerContents.ContainsKey(sourceReg) && _registerContents[sourceReg] is FieldDefinition fld)
                //This register contains a field definition. Return it
                return fld;

            //Don't read fields on arrays
            if (_registerContents.ContainsKey(sourceReg) && _registerContents[sourceReg] is ArrayData) return null;
            if (_registerTypes.ContainsKey(sourceReg) && _registerTypes[sourceReg]?.IsArray == true) return null;


            var isStatic = offset >= 0xb8;
            if (!isStatic)
            {
                _registerTypes.TryGetValue(sourceReg, out var type);
                if (type == null) return null;

                //TODO FUTURE: This accounts for about 2.75 seconds of execution time total (out of about 15 sec total processing time, i.e. about 12.5%, both times on my pc, for audica). A name => type dict would fix this.
                var typeDef = SharedState.AllTypeDefinitions.Find(t => t.FullName == type.FullName);
                if (typeDef == null)
                {
                    (typeDef, _) = Utils.TryLookupTypeDefByName(type.FullName);
                }

                if (typeDef == null) return null;

                var fields = SharedState.FieldsByType[typeDef];

                offset -= 0x10; //To account for the two internal il2cpp pointers

                var fieldRecord = fields.FirstOrDefault(f => f.Offset == (ulong) offset);

                if (fieldRecord.Offset != (ulong) offset) return null;

                var field = typeDef.Fields.FirstOrDefault(f => f.Name == fieldRecord.Name);

                return field;
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
                var fieldNum = (offset - 0xb8) / 8;

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
                case ud_mnemonic_code.UD_Imul:
                case ud_mnemonic_code.UD_Iimul:
                    return "Multiplies";
                case ud_mnemonic_code.UD_Isubss:
                case ud_mnemonic_code.UD_Isub:
                    return "Subtracts";
                case ud_mnemonic_code.UD_Iadd:
                    return "Adds";
                case ud_mnemonic_code.UD_Isar:
                case ud_mnemonic_code.UD_Ishr:
                    return "Shifts Right";
                default:
                    return "[Unknown Operation Name]";
            }
        }

        private string GetArithmeticOperationAssignment(ud_mnemonic_code code)
        {
            switch (code)
            {
                case ud_mnemonic_code.UD_Imulss:
                case ud_mnemonic_code.UD_Imul:
                case ud_mnemonic_code.UD_Iimul:
                    return "*=";
                case ud_mnemonic_code.UD_Isubss:
                case ud_mnemonic_code.UD_Isub:
                    return "-=";
                case ud_mnemonic_code.UD_Iadd:
                    return "+=";
                case ud_mnemonic_code.UD_Isar:
                case ud_mnemonic_code.UD_Ishr:
                    return ">>";
                default:
                    return "[Unknown Operation Name]";
            }
        }

        private void ShiftStack(int changeAmount)
        {
            _typeDump.Append($" ; - all stack values are shifted by 0x{changeAmount:X}");
            var newAliases = new Dictionary<int, string>();
            foreach (var keyValuePair in _stackAliases)
                newAliases[keyValuePair.Key + changeAmount] = keyValuePair.Value;

            _stackAliases = newAliases;

            var newTypes = new Dictionary<int, TypeDefinition>();
            foreach (var keyValuePair in _stackTypes)
                newTypes[keyValuePair.Key + changeAmount] = keyValuePair.Value;

            _stackTypes = newTypes;
        }

        private void CheckForArithmeticOperations(Instruction instruction)
        {
            //OH BOY SPECIAL CASES? WE LOVE THOSE!
            //WHY THE HELL IS IMUL A THING. I WANT TO SHOOT SOMEONE.
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
                        destReg = GetRegisterName(instruction.Operands[0]);
                        (firstArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);
                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                        // firstSourceReg = GetRegisterName(instruction.Operands[0]);
                        // secondSourceReg = GetRegisterName(instruction.Operands[1]);
                        break;
                    case 3:
                        destReg = GetRegisterName(instruction.Operands[0]);
                        // firstSourceReg = GetRegisterName(instruction.Operands[1]);
                        // secondSourceReg = GetRegisterName(instruction.Operands[2]);
                        (firstArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[2], instruction);
                        break;
                    default:
                        throw new Exception("WHAT THE HELL MATHS DOESN'T EXIST ANYMORE.");
                }

                var localName = $"local{_localNum}";
                _localNum++;

                // if (!_registerAliases.TryGetValue(firstSourceReg, out var firstArgName))
                //     firstArgName = $"[value in {firstSourceReg}]";
                //
                // if (!_registerAliases.TryGetValue(secondSourceReg, out var secondArgName))
                //     secondArgName = $"[value in {secondSourceReg}]";
                
                _registerAliases[destReg] = localName;

                _typeDump.Append("; - identified and processed one of them there godforsaken imul instructions.");
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(LongReference.FullName).Append(" ").Append(localName).Append(" = ").Append(firstArgName).Append(" * ").Append(secondArgName).Append("\n");
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Multiplies {firstArgName} by {secondArgName} and stores the result in new local {localName} in register {destReg}\n");

                return;
            }

            //Need 2 operand
            if (instruction.Operands.Length < 2) return;

            //RSP operations are special cases
            if (GetRegisterName(instruction.Operands[0]) == "rsp")
            {
                var (_, _, a) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);

                int changeAmount;

                if (!(a is ulong amount)) return;

                //Need to adjust stack pointer, and only need to handle add and sub
                if (instruction.Mnemonic == ud_mnemonic_code.UD_Iadd)
                {
                    _typeDump.Append($" ; - increases stack pointer by 0x{a:X}, so all current stack indexes are decreased by that much.");
                    changeAmount = (int) (0 - amount);
                    //TODO: Actually adjust pointers - iterate on stack maps.
                }
                else if (instruction.Mnemonic == ud_mnemonic_code.UD_Isub)
                {
                    _typeDump.Append($" ; - decreases stack pointer by 0x{a:X}, so all current stack indexes are increased by that much.");
                    changeAmount = (int) amount;
                }
                else
                    return;

                ShiftStack(changeAmount);

                return;
            }

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
                    uint rawAddr = _cppAssembly.MapVirtualAddressToRaw(virtualAddress);
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

                    var destReg = GetRegisterName(instruction.Operands[0]);

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

                    var destReg = GetRegisterName(instruction.Operands[0]);

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

        private void CheckForBooleanInvert(Instruction instruction)
        {
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Isetz) return;

            if (instruction.Operands.Length != 1) return;

            var reg = GetRegisterName(instruction.Operands[0]);

            _registerTypes.TryGetValue(reg, out var regType);
            _registerAliases.TryGetValue(reg, out var regAlias);

            if (regType?.Name != "Boolean") return;

            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Inverts {regAlias}\n");
            _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}{regAlias} = !{regAlias}\n");
        }

        private void CheckForFieldWrites(Instruction instruction)
        {
            //Need 2 operands
            if (instruction.Operands.Length < 2) return;

            //First/destination has to be a memory offset
            if (instruction.Operands[0].Type != ud_type.UD_OP_MEM) return;

            var destReg = GetRegisterName(instruction.Operands[0]);

            _registerAliases.TryGetValue(destReg, out var destAlias);
            var destinationField = GetFieldReferencedByOperand(instruction.Operands[0]);
            var destinationType = destinationField?.FieldType;
            string destinationFullyQualifiedName = null;

            if (destinationField == null && _registerTypes.ContainsKey(destReg) && _registerContents.TryGetValue(destReg, out var arrayDestConst) && arrayDestConst is ArrayData arrayData)
            {
                //Check array write
                var (alias, type, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);
                if (alias.Contains("["))
                {
                    destinationType = type;
                    destinationFullyQualifiedName = alias;
                }
            }
            else if (destinationField != null)
            {
                if (destinationField.IsStatic)
                    destinationFullyQualifiedName = $"{destinationField.DeclaringType.FullName}.{destinationField.Name}";
                else
                    destinationFullyQualifiedName = $"{destAlias}.{destinationField.Name}";
            }

            if (destinationFullyQualifiedName == null || destinationType == null || instruction.Operands.Length <= 1 || instruction.Mnemonic == ud_mnemonic_code.UD_Icmp || instruction.Mnemonic == ud_mnemonic_code.UD_Itest) return;

            _typeDump.Append($" ; - Write into {destinationField?.FullName ?? destinationFullyQualifiedName}");

            var (sourceAlias, sourceType, constant) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);

            if (destinationType.IsPrimitive && constant is ulong num)
            {
                var bytes = BitConverter.GetBytes(num);
                if (destinationType.Name == "Int32")
                {
                    var integer = BitConverter.ToInt32(bytes, 0);
                    sourceAlias = integer.ToString(CultureInfo.InvariantCulture);
                    sourceType = IntegerReference;
                    constant = integer;
                }
                else
                {
                    var single = BitConverter.ToSingle(bytes, 0);
                    sourceAlias = single.ToString(CultureInfo.InvariantCulture);
                    sourceType = FloatReference;
                    constant = single;
                }
            }

            if (destinationType.Name == "Boolean" && constant is float f && Math.Abs(f) > -0.01 && Math.Abs(f) < 1.01)
            {
                //1 => true, 0 => false
                constant = Math.Abs(f - 1) > -0.1;
                sourceAlias = constant.ToString();
                sourceType = BooleanReference;
            }

            _typeDump.Append(" from ").Append(sourceAlias);

            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(destinationFullyQualifiedName).Append(" = ").Append(sourceAlias);
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Set {destinationFullyQualifiedName} (type {destinationType.FullName}) to {sourceAlias} (type {sourceType?.FullName})");

            //Either directly assignable, or is an array that the base type matches and the constant contains array data. Eugh. Damn you cecil for making this required.
            if (!destinationType.IsAssignableFrom(sourceType) && (!(destinationType is ArrayType arr) || !arr.GetElementType().IsAssignableFrom(sourceType) || !_registerContents.TryGetValue(GetRegisterName(instruction.Operands[1]), out var cons) || !(cons is ArrayData)))
            {
                _typeDump.Append($" ; - Field type mismatch, {destinationType?.FullName} is not assignable from {sourceType?.FullName}");
                TaintMethod(TaintReason.FIELD_TYPE_MISMATCH);
            }
            else
            {
                _psuedoCode.Append("\n");
                _methodFunctionality.Append("\n");
            }
        }

        private void CheckForFieldArrayAndStackReads(Instruction instruction)
        {
            //Pre-checks
            if (instruction.Operands.Length < 2 || instruction.Operands[1].Type != ud_type.UD_OP_MEM || instruction.Operands[1].Base == ud_type.UD_R_RIP) return;

            //Don't handle arithmetic
            if (_inlineArithmeticOpcodes.Contains(instruction.Mnemonic)) return;

            //Special case: Stack pointers
            if (instruction.Operands[1].Base == ud_type.UD_R_RSP)
            {
                var stackOffset = Utils.GetOperandMemoryOffset(instruction.Operands[1]);
                if (instruction.Mnemonic == ud_mnemonic_code.UD_Ilea)
                {
                    //Register now has a pointer to the stack
                    var destReg = GetRegisterName(instruction.Operands[0]);
                    _typeDump.Append($" ; - Move reference to value in stack at offset 0x{stackOffset:X} to reg {destReg}");

                    _registerAliases[destReg] = $"stackPtr_0x{stackOffset:X}";
                    _registerContents[destReg] = new StackPointer(stackOffset);

                    var type = LongReference;

                    if (_stackTypes.TryGetValue(stackOffset, out var t))
                        type = t;

                    _registerTypes[destReg] = type;
                }
                else if (_moveOpcodes.Contains(instruction.Mnemonic))
                {
                    //Register now has the value from the stack
                    var destReg = GetRegisterName(instruction.Operands[0]);

                    var key = _stackAliases.ContainsKey(stackOffset) ? _stackAliases[stackOffset] : $"unknown_stack_val_0x{stackOffset:X}";

                    _registerAliases[destReg] = key;

                    _typeDump.Append($" ; - Move value in stack at offset 0x{stackOffset:X} (which is {key}) to reg {destReg}");

                    _registerContents.TryRemove(destReg, out _); //TODO: need to handle consts?

                    var type = LongReference;

                    if (_stackTypes.TryGetValue(stackOffset, out var t))
                        type = t;

                    _registerTypes[destReg] = type;
                }
                else
                {
                    _typeDump.Append($" ; - do something with stack pointer at offset 0x{stackOffset:X} into the stack");
                }

                return;
            }

            //Check for field read
            var field = GetFieldReferencedByOperand(instruction.Operands[1]);

            var sourceReg = GetRegisterName(instruction.Operands[1]);

            if (Utils.GetOperandMemoryOffset(instruction.Operands[1]) == 0)
            {
                //Just a register assignment, but as a mem op. It happens. And it's probably SharpDisasm's fault.
                return;
            }

            if (field != null && instruction.Operands[0].Type == ud_type.UD_OP_REG)
            {
                _typeDump.Append($" ; - field read on {field} from type {field.DeclaringType.Name} in register {sourceReg}");

                //Compares don't create locals
                if (instruction.Mnemonic == ud_mnemonic_code.UD_Icmp || instruction.Mnemonic == ud_mnemonic_code.UD_Itest) return;

                var destReg = GetRegisterName(instruction.Operands[0]);

                CreateLocalFromField(sourceReg, field, destReg);
            }
            else if (field == null)
            {
                var fieldReadOffset = Utils.GetOperandMemoryOffset(instruction.Operands[1]);

                //For some godawful reason the compiler sometimes uses LEA on a known constant and offset to get another constant (e.g. LEA dest, [regWithNullPtr+0x01] => put 1 into dest; LEA dest, [regWith1-0x02] => put -1 into dest; etc)
                if (instruction.Mnemonic == ud_mnemonic_code.UD_Ilea && _registerContents.ContainsKey(sourceReg) && _registerContents[sourceReg].GetType().IsPrimitive)
                {
                    try
                    {
                        var constant = (int) _registerContents[sourceReg];

                        var result = constant + fieldReadOffset;

                        var destReg = GetRegisterName(instruction.Operands[0]);

                        _typeDump.Append($" ; - Move of constant value {{result}} into {destReg}");

                        _registerContents[destReg] = result;
                        _registerTypes[destReg] = IntegerReference;
                        _registerAliases[destReg] = $"local{_localNum}";
                        _localNum++;

                        _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates local variable {_registerAliases[destReg]} in {destReg} with constant value {result}\n");
                        _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(IntegerReference.FullName).Append(" ").Append(_registerAliases[destReg]).Append(" = ").Append(result).Append("\n");
                        return;
                    }
                    catch (InvalidCastException)
                    {
                        //Ignore
                    }
                }

                //Arrays
                if (_registerTypes.TryGetValue(sourceReg, out var typeInReg) && typeInReg != null)
                {
                    var arrayIndex = -1;
                    var arrayLength = int.MaxValue;
                    TypeReference arrayType = null;

                    typeInReg = SharedState.AllTypeDefinitions.Find(t => t.FullName == typeInReg.FullName);
                    
                    SharedState.FieldsByType.TryGetValue(typeInReg, out var fieldsForTypeInReg);
                    var lastFieldOptional = fieldsForTypeInReg?.LastOrDefault();

                    //Array handling - if the last field in an object is an array it MAY be inline (see: string) and we can treat it as an array read
                    if (fieldsForTypeInReg != null && lastFieldOptional.Value.Type?.IsArray == true)
                    {
                        var lastField = lastFieldOptional.Value;
                        
                        //Ok, we have an array type.
                        //This is experimental, but should work for il2cpp-generated checks and optimizations (can i can an "ew", anyone?)
                        _typeDump.Append(" ; - last field is an array and we couldn't directly resolve a field");
                        if ((ulong) fieldReadOffset > lastField.Offset)
                        {
                            _typeDump.Append(" - and the index we're trying to read is greater than the last field. Looking like an inline array read");

                            arrayType = lastField.Type.GetElementType();

                            var arrayReadOffset = fieldReadOffset - 0x10 - (int) lastField.Offset;
                            var sizeOfObject = Utils.GetSizeOfObject(arrayType);

                            arrayIndex = arrayReadOffset / (int) sizeOfObject;

                            _typeDump.Append($" - array type is {arrayType}, read offset is {arrayReadOffset}, size of object is {sizeOfObject}, leaving array index {arrayIndex}");
                        }
                    }
                    else if (_registerContents.TryGetValue(sourceReg, out var cons) && (cons is ArrayData || typeInReg.IsArray))
                    {
                        var arrayOffset = Utils.GetOperandMemoryOffset(instruction.Operands[1]);

                        _typeDump.Append(" ; - array operation");

                        arrayIndex = (arrayOffset - 0x20) / 8;

                        if (cons is ArrayData arrayData)
                        {
                            //If we have this situation then the constant tells us the length of the array
                            arrayLength = (int) arrayData.Length;
                            arrayType = arrayData.ElementType;
                        }
                        else
                        {
                            arrayType = typeInReg.GetElementType();
                        }
                    }

                    if (arrayIndex >= 0)
                    {
                        //We succeeding in resolving an array index, process it.
                        try
                        {
                            if (arrayIndex == arrayLength)
                            {
                                //Accessing one more value than we have is used to get the type of the array
                                var destReg = GetRegisterName(instruction.Operands[0]);

                                _registerTypes[destReg] = TypeReference;
                                _registerAliases[destReg] = $"local{_localNum}";
                                _registerContents[destReg] = arrayType?.GetElementType();
                                _localNum++;

                                _typeDump.Append($" ; - loads the type of the array ({arrayType?.FullName}) into {destReg}");

                                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(TypeReference.FullName).Append(" ").Append(_registerAliases[destReg]).Append(" = ").Append(_registerAliases[sourceReg]).Append(".GetType().GetElementType() //Get the type of the array\n");
                                _methodFunctionality.Append(
                                    $"{Utils.Repeat("\t", _blockDepth + 2)}Loads the element type of the array {_registerAliases[sourceReg]} stored in {sourceReg} (which is {arrayType?.FullName}) and stores it in a new local {_registerAliases[destReg]} in register {destReg}\n");
                            }
                            else if (arrayIndex >= 0 && arrayIndex < arrayLength)
                            {
                                var destReg = GetRegisterName(instruction.Operands[0]);
                                _registerAliases.TryGetValue(sourceReg, out var arrayAlias);
                                _typeDump.Append($" ; - reads the value at index {arrayIndex} into the array {arrayAlias} stored in {sourceReg}");

                                var localName = $"local{_localNum}";
                                _registerAliases[destReg] = localName;
                                _registerTypes[destReg] = arrayType?.Resolve();
                                _registerContents.TryRemove(destReg, out _);

                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Reads the value at index {arrayIndex} into the array {arrayAlias} and stores the result in a new local {localName} of type {arrayType?.FullName} in {destReg}\n");
                                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}{arrayType?.FullName} {localName} = {arrayAlias}[{arrayIndex}]\n");

                                _localNum++;
                            }
                            else if (arrayIndex == 0x18)
                            {
                                //Array length 
                                var destReg = GetRegisterName(instruction.Operands[0]);

                                _registerAliases[destReg] = $"local{_localNum}";
                                _registerTypes[destReg] = IntegerReference;

                                _typeDump.Append($" ; - reads the length of the array {_registerAliases[sourceReg]}");
                                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}{IntegerReference.FullName} {_registerAliases[destReg]} = {_registerAliases[sourceReg]}.Length\n");
                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Reads the length of the array {_registerAliases[sourceReg]} (in {sourceReg}) into new local {_registerAliases[destReg]} in {destReg}\n");

                                _localNum++;
                            }

                            return;
                        }
                        catch (InvalidCastException)
                        {
                            //Ignore
                        }
                    }
                    else if (_registerTypes.TryGetValue(sourceReg, out var type))
                    {
                        _registerAliases.TryGetValue(sourceReg, out var alias);
                        _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}UNKNOWN FIELD READ (address 0x{fieldReadOffset:X}, reg alias {alias}, type {type?.FullName}, in register {sourceReg})\n");
                        _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}//Missing a field read on {alias ?? "[something]"} here.\n");
                    }
                }
                else
                {
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}UNKNOWN FIELD READ (unknown type in register {sourceReg}!)\n");
                }

                TaintMethod(TaintReason.UNRESOLVED_FIELD);
                _typeDump.Append($" ; - field read on unknown field from an unknown/unresolved type that's in reg {sourceReg}");
            }
            else
            {
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}WARN: Field Read: Don't know what we're doing with field {field} as first operand is not a register\n");
                TaintMethod(TaintReason.UNRESOLVED_FIELD);
                _typeDump.Append(" ; - field read and unknown action");
            }
        }

        private void CreateLocalFromField(string sourceReg, FieldDefinition field, string destReg)
        {
            if (!_registerAliases.TryGetValue(sourceReg, out var sourceAlias))
            {
                sourceAlias = $"[value in {sourceReg} -- why don't we have a name??]";
                TaintMethod(TaintReason.UNRESOLVED_FIELD);
            }

            _registerTypes.TryGetValue(sourceReg, out var sourceType);

            var readType = Utils.TryLookupTypeDefByName(field.FieldType.FullName).Item1;

            _registerTypes[destReg] = readType;
            _registerAliases[destReg] = $"local{_localNum}";
            if (field.FieldType.IsArray)
                _registerContents[destReg] = new ArrayData(ulong.MaxValue, field.FieldType.GetElementType().Resolve() ?? LongReference);
            else
                _registerContents[destReg] = field;

            _typeDump.Append($" ; - creation of {_registerAliases[destReg]} (type {_registerTypes[destReg]}) from {field.FullName}");

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

        private void CheckForRegClear(Instruction instruction)
        {
            //Must be an xor or xorps
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Ixor && instruction.Mnemonic != ud_mnemonic_code.UD_Ixorps) return;

            //Must be dealing with 2 registers
            if (instruction.Operands[0].Type != ud_type.UD_OP_REG || instruction.Operands[1].Type != ud_type.UD_OP_REG) return;

            //And both must be the same
            if (instruction.Operands[0].Base != instruction.Operands[1].Base) return;

            var reg = GetRegisterName(instruction.Operands[1]);

            _typeDump.Append($" ; zero out register {reg}");

            //Zeroed out, so literally set it to zero/int32
            if (instruction.Mnemonic == ud_mnemonic_code.UD_Ixorps)
            {
                _registerContents[reg] = 0.0f;
                _registerTypes[reg] = FloatReference;
            }
            else
            {
                _registerContents[reg] = 0;
                _registerTypes[reg] = IntegerReference;
            }

            if (!(_loopRegisters.Contains(reg) && _registerAliases.ContainsKey(reg)))
            {
                _registerAliases[reg] = $"local{_localNum}";
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates local variable local{_localNum} with value 0, in register {reg}\n");
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(_registerTypes[reg].FullName).Append(" ").Append($"local{_localNum} = 0\n");
                _localNum++;
            }
            else
            {
                //Slight Hack: Preserve types for suspected loop registers/counters.
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(_registerTypes[reg].FullName).Append(" ").Append(_registerAliases[reg]).Append(" = 0\n");
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates local variable {_registerAliases[reg]} with value 0, in register {reg}\n");
            }
        }

        private void CheckForMoveIntoRegister(Instruction instruction)
        {
            //Checks

            //Need 2 operands
            if (instruction.Operands.Length < 2) return;

            //Destination must be a register or a stack addr
            if (instruction.Operands[0].Type != ud_type.UD_OP_REG && (instruction.Operands[0].Type != ud_type.UD_OP_MEM || instruction.Operands[0].Base != ud_type.UD_R_RSP)) return;

            //Must be some sort of move
            if (!_moveOpcodes.Contains(instruction.Mnemonic)) return;

            var destReg = GetRegisterName(instruction.Operands[0]);
            var isStack = destReg == "rsp";
            var stackAddr = isStack ? Utils.GetOperandMemoryOffset(instruction.Operands[0]) : -1;

            //Ok now decide what to do based on what the second register is
            switch (instruction.Operands[1].Type)
            {
                case ud_type.UD_OP_REG:
                case ud_type.UD_OP_MEM when Utils.GetOperandMemoryOffset(instruction.Operands[1]) == 0:
                    //Simple case, reg => reg/stack
                    var sourceReg = GetRegisterName(instruction.Operands[1]);
                    if (_registerAliases.ContainsKey(sourceReg))
                    {
                        string newAlias;
                        string sourceAlias;
                        // if (instruction.Mnemonic == ud_mnemonic_code.UD_Imovzx)
                        // {
                        //     //Negated move, need to make a local
                        //     //TODO: This appears to just be "move zero-extended", but it also appears to negate booleans - how does this work?
                        //     //TODO: This ^ might be mov__S__x (which is move sign-extended) which prepends a 1, which MIGHT account for this behavior. Investigate further.
                        //     newAlias = $"local{_localNum}";
                        //     sourceAlias = "!" + _registerAliases[sourceReg];
                        //
                        //     _typeDump.Append($" ; - {newAlias} created from {sourceAlias}");
                        //     _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Negates {sourceAlias.Substring(1)} and stores the result in new local {newAlias}\n");
                        //     _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}System.Boolean {newAlias} = {sourceAlias}\n");
                        // }
                        // else
                        // {
                        sourceAlias = _registerAliases[sourceReg];
                        newAlias = sourceAlias;
                        // }

                        if (isStack)
                        {
                            _stackAliases[stackAddr] = newAlias;
                            _typeDump.Append($" ; - pushes {sourceAlias} onto the stack at address 0x{stackAddr:X}");
                        }
                        else
                            _registerAliases[destReg] = newAlias;

                        if (_registerTypes.ContainsKey(sourceReg))
                        {
                            if (isStack)
                                _stackTypes[stackAddr] = _registerTypes[sourceReg];
                            else
                            {
                                _registerTypes[destReg] = _registerTypes[sourceReg];
                                _typeDump.Append($" ; - {destReg} inherits {sourceReg}'s type {_registerTypes[sourceReg]}");
                            }
                        }

                        if (_registerContents.ContainsKey(sourceReg) && !isStack && sourceAlias == newAlias) //Check sourceAlias == newAlias so as to not copy constant in negations
                        {
                            _registerContents[destReg] = _registerContents[sourceReg];
                            _typeDump.Append($" ; - {destReg} inherits {sourceReg}'s constant value of {_registerContents[sourceReg]}");
                        }
                        else if (_registerContents.ContainsKey(destReg))
                        {
                            _registerContents.TryRemove(destReg, out _);
                            _typeDump.Append($" ; - {destReg} loses its constant value here due to being overwritten");
                        }
                    }
                    else if (!isStack && _registerAliases.ContainsKey(destReg))
                    {
                        _registerAliases.TryRemove(destReg, out _); //If we have one for the dest but not the source, clear the dest's alias.
                        _registerTypes.TryRemove(destReg, out _); //And its type
                    }

                    break;
                case ud_type.UD_OP_MEM when instruction.Operands[1].Base != ud_type.UD_R_RIP:
                    //Don't need to handle this case as it's a field read to reg which is done elsewhere
                    return;
                case ud_type.UD_OP_MEM when instruction.Operands[1].Base == ud_type.UD_R_RIP:
                    //TODO: do we ever actually do this with the stack? Might do via a reg first

                    //Reading either a global or a string literal into the register
                    var offset = Utils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]);
                    if (offset == 0) break;
                    var addr = _methodStart + offset;
                    _typeDump.Append($"; - Read on memory location 0x{addr:X}");
                    if (SharedState.GlobalsDict.TryGetValue(addr, out var glob))
                    {
                        _typeDump.Append($" - this is global value {glob.Name} of type {glob.IdentifierType}");
                        _registerAliases[destReg] = $"global_{glob.IdentifierType}_{glob.Name}";
                        _registerContents[destReg] = glob;
                        switch (glob.IdentifierType)
                        {
                            case AssemblyBuilder.GlobalIdentifier.Type.TYPE:
                                _registerTypes[destReg] = Utils.TryLookupTypeDefByName(glob.Name).Item1;
                                break;
                            case AssemblyBuilder.GlobalIdentifier.Type.METHOD:
                                _registerTypes.TryRemove(destReg, out _);
                                break;
                            case AssemblyBuilder.GlobalIdentifier.Type.FIELD:
                                _registerTypes.TryRemove(destReg, out _);
                                break;
                            case AssemblyBuilder.GlobalIdentifier.Type.LITERAL:
                                _registerTypes[destReg] = StringReference;
                                _typeDump.Append($" - therefore {destReg} now has type String");
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    else
                    {
                        //Try to read literal - used for native method name lookups and numerical constants
                        try
                        {
                            var actualAddress = _cppAssembly.MapVirtualAddressToRaw(addr);
                            _typeDump.Append(" - might be in file at " + actualAddress + $" ; memory block size is {instruction.Operands[1].Size}");

                            if (instruction.Operands[1].Size > 0)
                            {
                                //Try read const
                                _typeDump.Append(" ; - potentially an il2cpp numerical constant");

                                //MSBuild is really not happy about this line :(
                                var constBytes = _cppAssembly.raw.SubArray((int) Convert.ToInt32(actualAddress), /*in bits, convert to bytes*/ instruction.Operands[1].Size / 8);

                                object constant;
                                var constantType = LongReference;
                                switch (instruction.Operands[1].Size)
                                {
                                    case 8:
                                        constant = constBytes[0];
                                        constantType = ByteReference;
                                        break;
                                    case 16:
                                        constant = BitConverter.ToInt16(constBytes, 0);
                                        constantType = ShortReference;
                                        break;
                                    case 32:
                                        constant = BitConverter.ToInt32(constBytes, 0);
                                        constantType = IntegerReference;
                                        break;
                                    case 64:
                                        constant = BitConverter.ToInt64(constBytes, 0);
                                        constantType = LongReference;
                                        break;
                                    default:
                                        constant = null;
                                        break;
                                }

                                if (constant != null)
                                {
                                    _typeDump.Append($" ; - constant value of {constant} and type {constantType}");
                                    _registerAliases[destReg] = $"const_{constant}";
                                    _registerContents[destReg] = constant;
                                    _registerTypes[destReg] = constantType;
                                    return;
                                }
                            }

                            var literal = Utils.TryGetLiteralAt(_cppAssembly, (ulong) actualAddress);

                            if (literal != null)
                            {
                                _typeDump.Append(" - resolved as literal: " + literal);
                                _registerAliases[destReg] = literal;
                                _registerTypes[destReg] = StringReference;
                                _registerContents[destReg] = literal;
                                return;
                            }

                            //Clear register as we don't know what we're moving in
                            _registerAliases.TryRemove(destReg, out _);
                            _registerTypes.TryRemove(destReg, out _);
                            _registerContents.TryRemove(destReg, out _);
                        }
                        catch (Exception)
                        {
                            //suppress
                        }
                    }

                    break;
                case ud_type.UD_OP_IMM:
                    //Immediate - constant
                    _registerContents[destReg] = Utils.GetImmediateValue(instruction, instruction.Operands[1]);
                    _registerAliases[destReg] = _registerContents[destReg].ToString();
                    _registerTypes[destReg] = LongReference;
                    _typeDump.Append($" ; - Moves constant of value {_registerContents[destReg]} into {destReg}");
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
                HandleFunctionCall(method, true, instruction);
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

            if (instruction.Operands[0].Type == ud_type.UD_OP_MEM && instruction.Operands[0].Base != ud_type.UD_R_RIP)
            {
                //e.g. call [rax+0x178]
                //can't resolve this (TODO how do we?)
                //so flag it
                _typeDump.Append(" ; - Unresolvable call to calculated address.");
                TaintMethod(TaintReason.UNRESOLVED_METHOD);
                return;
            }

            ulong jumpAddress;
            try
            {
                jumpAddress = Utils.GetJumpTarget(instruction, _methodStart + instruction.PC);
                _typeDump.Append($" ; jump to 0x{jumpAddress:X}");
            }
            catch (Exception)
            {
                _typeDump.Append(" ; Exception occurred locating target");
                TaintMethod(TaintReason.UNRESOLVED_METHOD);
                return;
            }

            SharedState.MethodsByAddress.TryGetValue(jumpAddress, out var methodAtAddress);

            if (methodAtAddress != null)
            {
                HandleFunctionCall(methodAtAddress, true, instruction);
                return;
            }

            if (jumpAddress == _keyFunctionAddresses.AddrBailOutFunction)
            {
                _typeDump.Append(" - this is the bailout function and will be ignored.");
                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}THIS_IS_BAD_BAIL_OUT()\n");
                TaintMethod(TaintReason.NON_REMOVED_INTERNAL_FUNCTION);
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrInitFunction)
            {
                _typeDump.Append(" - this is the initialization function and will be ignored.");
                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}THIS_IS_BAD_INIT_CLASS()\n");
                TaintMethod(TaintReason.NON_REMOVED_INTERNAL_FUNCTION);
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrNewFunction)
            {
                _typeDump.Append(" - this is the constructor function.");
                var success = false;
                //Look up the global identifier in the constants dict
                if (_registerContents.TryGetValue("rcx", out var g) && g != null)
                {
                    //Check we actually have a global
                    if (g is AssemblyBuilder.GlobalIdentifier glob)
                    {
                        //Check it's valid (which it should be?)
                        if (glob.Offset != 0 && glob.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.TYPE)
                        {
                            //Look up type
                            var (definedType, genericParams) = Utils.TryLookupTypeDefByName(glob.Name);

                            if (definedType != null)
                            {
                                //If we've got it we can handle this as instance creation
                                var name = definedType.FullName;
                                if (genericParams.Length != 0)
                                    name = name.Replace($"`{genericParams.Length}", "");

                                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(name).AppendGenerics(genericParams).Append(" ").Append("local").Append(_localNum).Append(" = new ").Append(name).AppendGenerics(genericParams).Append("()\n");
                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an instance of type {name}{(genericParams.Length > 0 ? $" with generic parameters {string.Join(",", genericParams)}" : "")}\n");

                                PushMethodReturnTypeToLocal(definedType);
                                success = true;
                            }
                            else
                            {
                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an instance of (unresolved) type {glob.Name}\n");
                                success = true;
                            }
                        }
                        else
                        {
                            _typeDump.Append($" ; - but the global in rcx is not a type, it's a {glob.IdentifierType}");
                            TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                        }
                    }
                }
                else
                {
                    _typeDump.Append(" ; - but we don't have a global in register rcx?");
                    TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                }

                if (!success)
                {
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an instance of [something]\n");
                    TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                }
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrArrayCreation)
            {
                _typeDump.Append(" - this is the array creation function");
                var success = false;
                _registerAliases.TryGetValue("rcx", out var glob);
                _registerContents.TryGetValue("rdx", out var constant);
                if (glob != null && constant is ulong arraySize)
                {
                    var match = Regex.Match(glob, "global_([A-Z]+)_([^/]+)");
                    if (match.Success)
                    {
                        Enum.TryParse<AssemblyBuilder.GlobalIdentifier.Type>(match.Groups[1].Value, out var type);
                        var global = SharedState.Globals.Find(g => g.Name == match.Groups[2].Value && g.IdentifierType == type);
                        if (global.Offset != 0)
                        {
                            var (definedType, genericParams) = Utils.TryLookupTypeDefByName(global.Name.Replace("[]", ""));

                            if (definedType != null)
                            {
                                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(definedType.FullName).Append("[] ").Append("local").Append(_localNum).Append(" = new ").Append(definedType.FullName).Append("[").Append(arraySize).Append("]\n");
                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an array of type {definedType.FullName}[]{(genericParams.Length > 0 ? $" with generic parameters {string.Join(",", genericParams)}" : "")} of size {arraySize}\n");
                                PushMethodReturnTypeToLocal(ArrayReference);
                                _registerContents["rax"] = new ArrayData(arraySize, definedType); //Store array size in here for bounds checks
                                success = true;
                            }
                            else
                            {
                                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an array of (unresolved) type {global.Name} and size {arraySize}\n");
                                TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                                success = true;
                            }
                        }
                    }
                }

                if (!success)
                {
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Creates an array of [something]s and an unknown length\n");
                    TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                }
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrInitStaticFunction)
            {
                _typeDump.Append(" - this is the static class initializer and will be ignored");
                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}THIS_IS_BAD_STATIC_INIT_CLASS()\n");
                TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrNativeLookup)
            {
                _typeDump.Append(" - this is the native lookup function");
                _registerAliases.TryGetValue("rcx", out var functionName);

                //Native methods usually have an IL counterpart - but that just calls this method with its own name. Even so, we can point at that, for now.
                if (functionName == null) return;

                //Should be a FQ function name, but with the type and function separated with a ::, cpp style.
                var split = functionName.Split(new[] {"::"}, StringSplitOptions.None);

                if (split.Length < 2)
                {
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}WARN: NativeLookup: {functionName} does not have a :: in it, unable to process\n");
                    return;
                }

                var typeName = split[0];
                var (type, _) = Utils.TryLookupTypeDefByName(typeName);

                var methodName = split[1];
                methodName = methodName.Substring(0, methodName.IndexOf("(", StringComparison.Ordinal));

                MethodDefinition mDef = null;
                if (type != null)
                    mDef = type.Methods.First(mtd => mtd.Name.EndsWith(methodName));

                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Looks up native function by name {functionName} => {mDef?.FullName}\n");
                if (mDef == null)
                {
                    TaintMethod(TaintReason.UNRESOLVED_METHOD);
                    return;
                }

                _registerAliases["rax"] = $"{type.FullName}.{mDef.Name}";
                _registerContents["rax"] = mDef;
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrNativeLookupGenMissingMethod)
            {
                _typeDump.Append(" - this is the native lookup bailout function");
                TaintMethod(TaintReason.NON_REMOVED_INTERNAL_FUNCTION);
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrBoxValueMethod)
            {
                _typeDump.Append(" - this is the box value function.");
                //rcx has the destination type global, rdx has what we're casting
                _registerContents.TryGetValue("rcx", out var g);
                _registerAliases.TryGetValue("rdx", out var castTarget);
                _registerContents.TryGetValue("rdx", out var cSp); //Potential stack pointer

                if (cSp is StackPointer castStackPointer)
                {
                    _stackAliases.TryGetValue(castStackPointer.Address, out castTarget);
                    if (castTarget == null)
                    {
                        castTarget = $"[value in stack at 0x{castStackPointer.Address:X}]";
                        TaintMethod(TaintReason.UNRESOLVED_STACK_VAL);
                    }
                }

                if (g is AssemblyBuilder.GlobalIdentifier glob && glob.Offset != 0 && glob.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.TYPE)
                {
                    var destType = Utils.TryLookupTypeDefByName(glob.Name).Item1;
                    _typeDump.Append($" - Boxes the primitive value {castTarget} to {destType?.FullName} (resolved from {glob.Name})");
                    _registerAliases["rax"] = castTarget;
                    if (destType != null)
                        _registerTypes["rax"] = destType;
                    else
                    {
                        _registerTypes.TryRemove("rax", out _);
                        _typeDump.Append(" ; - failed to get dest type for box");
                        TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                    }
                }
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrSafeCastMethod)
            {
                _typeDump.Append(" - this is the safe cast function.");
                //rdx has the destination type, rcx has what we're casting
                _registerContents.TryGetValue("rdx", out var t);
                object castTarget;
                AssemblyBuilder.GlobalIdentifier globalIdentifier = default;
                _registerAliases.TryGetValue("rcx", out var castAlias);
                if (castAlias?.StartsWith("global") == true)
                {
                    _registerContents.TryGetValue("rcx", out var g);
                    if (g is AssemblyBuilder.GlobalIdentifier glob)
                    {
                        castTarget = glob.Name;
                        globalIdentifier = glob;
                    }
                    else
                        castTarget = g;
                }
                else
                    castTarget = castAlias;

                if (t is TypeDefinition type)
                {
                    _typeDump.Append($" - Safe casts {castTarget} to {type.FullName}");

                    //Push a local
                    _registerAliases["rax"] = $"local{_localNum}";
                    _registerContents["rax"] = globalIdentifier.Offset == 0 ? castTarget : globalIdentifier;
                    _registerTypes["rax"] = type;

                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Safe casts {castTarget} to new local {_registerAliases["rax"]}\n");
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(type.FullName).Append(" ").Append(_registerAliases["rax"]).Append(" = (").Append(type.FullName).Append(") ");

                    if (globalIdentifier.Offset == 0 || globalIdentifier.IdentifierType != AssemblyBuilder.GlobalIdentifier.Type.LITERAL)
                    {
                        _psuedoCode.Append(globalIdentifier.Name ?? castTarget);
                    }
                    else
                    {
                        _psuedoCode.Append($"'{globalIdentifier.Name}'");
                    }

                    _psuedoCode.Append("\n");
                }
                else
                {
                    TaintMethod(TaintReason.FAILED_TYPE_RESOLVE);
                    _typeDump.Append(" ; - failed to find safe cast target type");
                }
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrThrowMethod)
            {
                _typeDump.Append("; - this is the throw function");
                _registerAliases.TryGetValue("rcx", out var exceptionToThrowAlias);

                exceptionToThrowAlias ??= "[value in rcx]";

                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Throws {exceptionToThrowAlias}\n");
                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}throw {exceptionToThrowAlias};\n");
            }
            else if (jumpAddress == _keyFunctionAddresses.AddrPInvokeLookup)
            {
                _typeDump.Append("; - this is the p/invoke native entry point lookup function");
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Looks some p/invoke function up idk\n");
                _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}//P/invoke call goes here lol\n");
                //TODO: Work out how tf this works lol
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

                    var wasInIf = _currentBlockType == BlockType.IF;

                    PopBlock();

                    if (wasInIf)
                    {
                        //Now we need to find the ELSE length
                        //This current jump goes to the first instruction after it, so take its address - our PC to find function length
                        //TODO: This is ok, but still needs work in E.g AudioDriver_Resume
                        var instructionIdx = _instructions.FindIndex(i => i.PC == pos);
                        if (instructionIdx < 0) return;
                        instructionIdx++;
                        var numToIndent = instructionIdx - _instructions.IndexOf(instruction);

                        _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Else:\n"); //This is a +1 not a +2 to remove the indent caused by the current if block
                        _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append("else:\n");

                        PushBlock(numToIndent, BlockType.ELSE);
                    }
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

                            var genericTypes = genericParams.Select(Utils.TryLookupTypeDefByName).Select(t => t.Item1).ToList();

                            var method = definedType?.Methods?.FirstOrDefault(methd => methd.Name.Split('.').Last() == methodName);

                            if (method == null) continue;

                            var requiredCount = (method.IsStatic ? 0 : 1) + method.Parameters.Count;

                            if (requiredCount != providedParamCount) continue;

                            var returnType = Utils.TryLookupTypeDefByName(method.ReturnType.FullName).Item1;

                            if (returnType != null && returnType.Name == "Object" && genericTypes.Count == 1 && genericTypes.All(t => t != null))
                                returnType = genericTypes.First();

                            HandleFunctionCall(method, true, instruction, returnType);
                            return;
                        }
                    }

                    //See if we're throwing
                    if (!exceptionThrowerAddresses.TryGetValue(jumpAddress, out var exceptionType))
                    {
                        // Console.WriteLine($"Trying to find an exception throwing function at unknown function address 0x{jumpAddress:X}...");
                        var actualAddr = (ulong) _cppAssembly.MapVirtualAddressToRaw(jumpAddress);

                        var body = Utils.GetMethodBodyAt(_cppAssembly, actualAddr, peek: true);

                        var leas = body.Where(i => i.Mnemonic == ud_mnemonic_code.UD_Ilea).ToList();

                        var offsets = leas.Select(i => Utils.GetOffsetFromMemoryAccess(i, i.Operands[1])).ToList();

                        var targets = offsets.Select(offset => offset + jumpAddress).ToList();

                        // Console.WriteLine($"\tContains {targets.Count} LEA instructions...");

                        // _typeDump.Append($" ; - virtual targets are {targets.ToStringEnumerable()}");

                        var actualAddresses = targets.Select(addr => (ulong) _cppAssembly.MapVirtualAddressToRaw(addr)).Where(addr => addr != 0UL).ToList();

                        // _typeDump.Append($" ; - maps to real addresses {actualAddresses.ToStringEnumerable()}");

                        var literals = actualAddresses.Select(addr => Utils.TryGetLiteralAt(_cppAssembly, addr)).ToList();

                        // Console.WriteLine($"\tOr {literals.Count} string literals: {literals.ToStringEnumerable()}");

                        // _typeDump.Append($" ; - literals obtained: {literals.ToStringEnumerable()}");

                        if (literals.Count == 2)
                        {
                            //Exception, Namespace
                            var fqn = literals[1] + "." + literals[0];
                            _typeDump.Append($" ; - constructor for exception {fqn}");

                            (exceptionType, _) = Utils.TryLookupTypeDefByName(fqn);

                            _typeDump.Append($" ; - which resolves to {exceptionType?.FullName ?? "null"}");

                            exceptionThrowerAddresses[jumpAddress] = exceptionType;

                            if (exceptionType != null)
                                Console.WriteLine($"\tResolved exception generation function: 0x{jumpAddress:X} => {exceptionType.FullName}");
                        }
                        else
                        {
                            exceptionThrowerAddresses[jumpAddress] = null; //Mark as invalid
                        }
                    }

                    if (exceptionType != null)
                    {
                        //Got an exception
                        _registerContents.TryGetValue("rcx", out var message);
                        _typeDump.Append($"; - creates an {exceptionType.FullName}");

                        _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Invokes the exception construction function for exception type {exceptionType.FullName}");

                        if (message != null)
                            _methodFunctionality.Append($" with message {message}");
                        else
                            _methodFunctionality.Append(" with no message");

                        _methodFunctionality.Append("\n");

                        var destReg = PushMethodReturnTypeToLocal(exceptionType);
                        _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}{exceptionType.FullName} {_registerAliases[destReg]} = new {exceptionType.FullName}(");

                        if (message != null)
                        {
                            _typeDump.Append($" with message {message}");
                            _psuedoCode.Append('"').Append(message).Append('"');

                            if (message is string str && _keyFunctionAddresses.AddrPInvokeLookup == 0 && str.Contains("Unable to find method for p/invoke") && unknownMethodAddresses.Count == 1)
                            {
                                //TODO: We're gonna have to go back and do this method again or something
                                var addr = unknownMethodAddresses[0];
                                Console.WriteLine($"Found P/Invoke lookup function at 0x{addr:X} in method {_methodDefinition.FullName}");
                                _keyFunctionAddresses.AddrPInvokeLookup = addr;
                            }
                        }
                        else
                        {
                            _typeDump.Append(" with no message");
                            _psuedoCode.Append("null");
                        }

                        _psuedoCode.Append(")\n");
                        return;
                    }

                    //Got to here = no generic function
                    //so this is an unknown function call
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}WARN: Unknown function call to address 0x{jumpAddress:X} which might be in file 0x{_cppAssembly.MapVirtualAddressToRaw(jumpAddress):X}\n");
                    _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}UnknownFun_{jumpAddress:X}()\n");
                    TaintMethod(TaintReason.UNRESOLVED_METHOD);
                    unknownMethodAddresses.Add(jumpAddress);
                    if (instruction.Mnemonic == ud_mnemonic_code.UD_Ijmp)
                        //Jmp = not coming back to this function
                        _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}return\n");
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

            //Compiler generated weirdness. I hate it.
            if (instruction.Mnemonic == ud_mnemonic_code.UD_Icmp && instruction.Operands[1].Type == ud_type.UD_OP_IMM && GetFieldReferencedByOperand(instruction.Operands[0]) is { } field && Utils.GetImmediateValue(instruction, instruction.Operands[1]) == 0)
            {
                var registerName = GetRegisterName(instruction.Operands[0]);
                if (_registerAliases.ContainsKey(registerName))
                    CreateLocalFromField(registerName, field, "rax");
            }


            _lastComparison = new Tuple<(string, TypeDefinition, object), (string, TypeDefinition, object)>(GetDetailsOfReferencedObject(instruction.Operands[0], instruction), GetDetailsOfReferencedObject(instruction.Operands[1], instruction));

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
                TaintMethod(TaintReason.BAD_CONDITION);
                return;
            }

            var (comparisonItemA, typeA, _) = _lastComparison.Item1;
            var (comparisonItemB, typeB, constantB) = _lastComparison.Item2;

            //Try to do some type coercion such as int -> char
            if (TryCastToMatchType(typeA, ref typeB, ref constantB) && constantB != null)
            {
                comparisonItemB = constantB.ToString();
            }

            if (constantB is char)
                comparisonItemB = $"'{comparisonItemB}'";

            //TODO: Clear out crap [unknown global] if statements

            if (comparisonItemA.Contains("unknown global") || comparisonItemB.Contains("unknown global") || comparisonItemA.Contains("value in") || comparisonItemB.Contains("value in") || comparisonItemA.Contains("unknown refobject") || comparisonItemB.Contains("unknown refobject"))
                TaintMethod(TaintReason.BAD_CONDITION);

            var dest = Utils.GetJumpTarget(instruction, _methodStart + instruction.PC);

            //Going back to an earlier point in the function - loop.
            //This works because the compiler puts all if statements *after* the main function body for jumping forward to, then back.
            //Still need checks in those jump back cases.
            var isLoop = dest < instruction.PC + _methodStart && dest > _methodStart;

            // _lastComparison = new Tuple<object, object>("", ""); //Clear last comparison
            var checkTypes = true;
            string condition;
            switch (instruction.Mnemonic)
            {
                case ud_mnemonic_code.UD_Ijz:
                {
                    var isSelfCheck = Equals(comparisonItemA, comparisonItemB);
                    var isBoolean = typeA?.Name == "Boolean";
                    if (isBoolean)
                        condition = isSelfCheck ? $"{comparisonItemA} == false" : $"{comparisonItemA} == {comparisonItemB}";
                    else if (typeA?.IsPrimitive == true || typeA?.IsEnum == true)
                        condition = isSelfCheck ? $"{comparisonItemA} == 0" : $"{comparisonItemA} == {(constantB is ulong i && i == 0 ? "0" : comparisonItemB)}";
                    else
                        condition = isSelfCheck ? $"{comparisonItemA} == null" : $"{comparisonItemA} == {(constantB is ulong i && i == 0 ? "null" : comparisonItemB)}";

                    if (isSelfCheck)
                        checkTypes = false;

                    break;
                }
                case ud_mnemonic_code.UD_Ijnz:
                {
                    var isSelfCheck = Equals(comparisonItemA, comparisonItemB);
                    var isBoolean = typeA?.Name == "Boolean";
                    if (isBoolean)
                        condition = isSelfCheck ? $"{comparisonItemA} == true" : $"{comparisonItemA} != {comparisonItemB}";
                    else if (typeA?.IsPrimitive == true || typeA?.IsEnum == true)
                        condition = isSelfCheck ? $"{comparisonItemA} != 0" : $"{comparisonItemA} != {(constantB is ulong i && i == 0 ? "0" : comparisonItemB)}";
                    else
                        condition = isSelfCheck ? $"{comparisonItemA} != null" : $"{comparisonItemA} != {(constantB is ulong i && i == 0 ? "null" : comparisonItemB)}";

                    if (isSelfCheck)
                        checkTypes = false;

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
                    TaintMethod(TaintReason.BAD_CONDITION);
                    break;
            }

            if (condition == null) return;

            if (checkTypes && typeA?.FullName != typeB?.FullName)
            {
                if (constantB is ulong i2 && i2 == 0)
                {
                    //Null literal, this is fine
                }
                else if (typeA?.IsEnum == true && (typeB?.IsPrimitive == true || constantB?.GetType().IsPrimitive == true)
                         || typeB?.IsEnum == true && (typeA?.IsPrimitive == true || constantB?.GetType().IsPrimitive == true))
                {
                    //Comparing an enum to a primitive is fine because some enum values are stripped :(
                }
                else
                {
                    _typeDump.Append($" ; - nonsensical comparison, typeA is {typeA?.FullName}, typeB is {typeB?.FullName}, constantB is {constantB} of type {constantB?.GetType()}");
                    TaintMethod(TaintReason.NONSENSICAL_COMPARISON);
                }
            }

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
                    TaintMethod(TaintReason.MISSING_IF_BODY);
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

                            var lastCount = _indentCounts.Count == 0 ? int.MaxValue : _indentCounts.Last();

                            var toAdd = Math.Min(_indentCounts.Count > 0 ? _indentCounts.Min() : int.MaxValue, numToIndent);
                            if (lastCount <= 2)
                            {
                                _indentCounts.RemoveAt(_indentCounts.Count - 1);
                                toAdd = Math.Min(_indentCounts.Count > 0 ? _indentCounts.Min() : int.MaxValue, numToIndent);
                                _indentCounts.Add(toAdd);
                            }

                            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}If {Utils.InvertCondition(condition)} {{\n");
                            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append("if (").Append(Utils.InvertCondition(condition)).Append(") {\n");

                            PushBlock(toAdd, BlockType.IF);

                            return;
                        }
                    }

                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Jumps to 0x{dest:X} if {condition}\n");
                }
            }
        }

        private bool TryCastToMatchType(TypeDefinition castToMatch, ref TypeDefinition originalType, ref object constantValue)
        {
            if (originalType == null || castToMatch == null) return false; //Invalid call to this function

            if (originalType.FullName == castToMatch.FullName) return false; //No need to do anything

            //Numerical to char
            if (castToMatch.FullName == "System.Char" && constantValue != null && int.TryParse(constantValue.ToString(), out var constantInt))
            {
                constantValue = (char) constantInt;
                originalType = castToMatch;

                return true;
            }

            //Different integer types can be freely cast between
            if (castToMatch.IsPrimitive && originalType.IsPrimitive)
            {
                originalType = castToMatch;
                
                if(constantValue != null && long.TryParse(constantValue.ToString(), out var constantLong))
                    constantValue = constantLong;

                return true;
            }

            return false;
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

        private void CheckForPushPop(Instruction instruction)
        {
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Ipush && instruction.Mnemonic != ud_mnemonic_code.UD_Ipop)
                return;

            if (instruction.Mnemonic == ud_mnemonic_code.UD_Ipush)
            {
                //Push to stack & shift stack further away (as we're decrementing stack pointer)
                ShiftStack(8); //TODO Need a changed offset or is this always ok? And do we need to ever actually copy alias over etc?
            }
            else if (instruction.Mnemonic == ud_mnemonic_code.UD_Ipop)
            {
                //Pop from stack and shift closer (as we're incrementing stack pointer)
                ShiftStack(-8);
            }
        }

        private void CheckForReturn(Instruction instruction)
        {
            if (instruction.Mnemonic != ud_mnemonic_code.UD_Iret && instruction.Mnemonic != ud_mnemonic_code.UD_Iretf) return;

            //What do we return?
            string returnAlias = null;
            object returnConstant = null;
            if (_methodDefinition.ReturnType.Name != "Void")
            {
                if (Utils.ShouldBeInFloatingPointRegister(_methodDefinition.ReturnType))
                {
                    if (_registerContents.TryGetValue("xmm0", out returnConstant))
                        if (!_registerAliases.TryGetValue("xmm0", out returnAlias))
                            returnAlias = "[value in xmm0]";
                }
                else if (_registerContents.TryGetValue("rax", out returnConstant))
                    if (!_registerAliases.TryGetValue("rax", out returnAlias))
                        returnAlias = "[value in rax]";
            }

            if (returnConstant != null)
            {
                if (returnConstant is AssemblyBuilder.GlobalIdentifier glob && glob.IdentifierType == AssemblyBuilder.GlobalIdentifier.Type.LITERAL)
                    returnConstant = glob.Name;

                if (returnConstant is string)
                    returnConstant = $"\"{returnConstant}\"";

                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Returns {returnConstant}\n");
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append($"return {returnConstant}\n");
            }
            else if (returnAlias != null)
            {
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Returns {returnAlias}\n");
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append($"return {returnAlias}\n");
            }
            else
            {
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Returns\n");
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append("return\n");
            }
        }

        private AssemblyBuilder.GlobalIdentifier? GetGlobalInReg(string reg)
        {
            if (_registerContents.TryGetValue(reg, out var g) && g is AssemblyBuilder.GlobalIdentifier glob && glob.Offset != 0)
                return glob;

            if (_registerAliases.TryGetValue(reg, out var globAlias) && globAlias != null)
            {
                var match = Regex.Match(globAlias, "global_([A-Z]+)_([^/]+)");
                if (match.Success)
                {
                    Enum.TryParse<AssemblyBuilder.GlobalIdentifier.Type>(match.Groups[1].Value, out var type);
                    var global = SharedState.Globals.Find(g => g.Name == match.Groups[2].Value && g.IdentifierType == type);
                    if (global.Offset != 0)
                    {
                        return global;
                    }
                }
            }

            return null;
        }

        private struct PreBlockCache
        {
            public ConcurrentDictionary<string, string> Aliases;
            public ConcurrentDictionary<string, TypeDefinition> Types;
            public ConcurrentDictionary<string, object> Constants;
            public BlockType BlockType;

            public PreBlockCache(ConcurrentDictionary<string, string> registerAliases, ConcurrentDictionary<string, object> registerContents, ConcurrentDictionary<string, TypeDefinition> registerTypes, BlockType type)
            {
                Aliases = new ConcurrentDictionary<string, string>();
                Types = new ConcurrentDictionary<string, TypeDefinition>();
                Constants = new ConcurrentDictionary<string, object>();

                foreach (var keyValuePair in registerAliases) Aliases[keyValuePair.Key] = keyValuePair.Value;
                foreach (var registerContent in registerContents) Constants[registerContent.Key] = registerContent.Value;
                foreach (var registerType in registerTypes) Types[registerType.Key] = registerType.Value;

                BlockType = type;
            }
        }

        private class StackPointer
        {
            public readonly int Address;

            public StackPointer(int address)
            {
                Address = address;
            }
        }

        private class ArrayData
        {
            public readonly ulong Length;
            public readonly TypeDefinition ElementType;

            public ArrayData(ulong length, TypeDefinition elementType)
            {
                Length = length;
                ElementType = elementType;
            }
        }

        private enum BlockType
        {
            NONE,
            IF,
            ELSE
        }

        internal enum TaintReason
        {
            UNTAINTED = 0,
            UNRESOLVED_METHOD = 1,
            UNRESOLVED_FIELD = 2,
            UNRESOLVED_STACK_VAL = 3,
            BAD_CONDITION = 4,
            METHOD_PARAM_MISSING = 5,
            NON_REMOVED_INTERNAL_FUNCTION = 6,
            FAILED_TYPE_RESOLVE = 7,
            METHOD_PARAM_MISMATCH = 8,
            FIELD_TYPE_MISMATCH = 9,
            NONSENSICAL_COMPARISON = 10,
            MISSING_IF_BODY = 11,
        }
    }
}