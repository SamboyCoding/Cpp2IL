using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    internal class ASMDumper
    {
        private readonly MethodDefinition _methodDefinition;
        private readonly CppMethodData _method;
        private readonly ulong _methodStart;
        private readonly List<AssemblyBuilder.GlobalIdentifier> _globals;
        private readonly KeyFunctionAddresses _keyFunctionAddresses;
        private readonly PE.PE _cppAssembly;
        private Dictionary<string, string> _registerAliases;
        private Dictionary<string, TypeReference> _registerTypes;
        private StringBuilder _methodFunctionality;
        private StringBuilder _typeDump;

        internal ASMDumper(MethodDefinition methodDefinition, CppMethodData method, ulong methodStart, List<AssemblyBuilder.GlobalIdentifier> globals, KeyFunctionAddresses keyFunctionAddresses, PE.PE cppAssembly)
        {
            _methodDefinition = methodDefinition;
            _method = method;
            _methodStart = methodStart;
            _globals = globals;
            _keyFunctionAddresses = keyFunctionAddresses;
            _cppAssembly = cppAssembly;
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

                ret.Add(insn);
            }

            return ret;
        }

        internal void DumpMethod(StringBuilder typeDump, ref List<ud_mnemonic_code> allUsedMnemonics)
        {
            _typeDump = typeDump;
            //As we're on windows, function params are passed RCX RDX R8 R9, then the stack
            //If these are floating point numbers, they're put in XMM0 to 3
            //Register eax/rax/whatever you want to call it is the return value (both of any functions called in this one and this function itself)

            typeDump.Append($"Method: {_methodDefinition.FullName}: (");

            _methodFunctionality = new StringBuilder();

            //Pass 0: Disassemble
            var instructions = Utils.DisassembleBytes(_method.MethodBytes);

            //Pass 1: Removal of unneeded generated code
            instructions = TrimOutIl2CppCrap(instructions);

            //Pass 2: Loop Detection
            var loopRegisters = DetectPotentialLoops(instructions);

            if (loopRegisters.Count > 0)
                _methodFunctionality.Append($"\t\tPotential Loops Centred on Register(s): {string.Join(",", loopRegisters)}\n");

            var distinctMnemonics = new List<ud_mnemonic_code>(instructions.Select(i => i.Mnemonic).Distinct());
            typeDump.Append($"uses {distinctMnemonics.Count} unique operations)\n");
            allUsedMnemonics = new List<ud_mnemonic_code>(allUsedMnemonics.Concat(distinctMnemonics).Distinct());

            _registerAliases = new Dictionary<string, string>();
            _registerTypes = new Dictionary<string, TypeReference>();

            //Dump params
            typeDump.Append("\tParameters in registers: \n");

            var registers = new List<string>(new[] {"rcx/xmm0", "rdx/xmm1", "r8/xmm2", "r9/xmm3"});
            var stackIndex = 0;

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

            foreach (var parameter in _methodDefinition.Parameters)
            {
                string pos;
                var isReg = false;
                if (registers.Count > 0)
                {
                    pos = registers[0];
                    isReg = true;
                    registers.RemoveAt(0);
                    foreach (var reg in pos.Split('/'))
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

            var knownRegisters = new List<string>(new[] {"rsp", "rbp", "rsi", "rdi", "rbx", "rax"});
            var argumentRegisters = new List<string>();
            typeDump.Append("\tMethod Body (x86 ASM):\n");

            //In terms of functionality:
            //    Pushes/Pops can (probably) be ignored.
            //    Mov statements that serve only to preserve registers can be ignored (e.g backing up an xmm* register)
            //    Function calls can be inlined as such.
            //    Space reserving (add rsp / sub rsp) can be ignored.

            var fieldAssignmentRegex = new Regex(@"\[(\w+)_(\w+)\+0x(\d+)\], (\w+)_(\w+)");

            var localNum = 1;
            foreach (var instruction in instructions)
            {
                //Preprocessing to make it easier to read.
                var line = instruction.ToString();
                line = _registerAliases.Aggregate(line, (current, kvp) => current.Replace(kvp.Key, $"{kvp.Value}_{kvp.Key}"));

                //Detect field writes into local class
                var m = fieldAssignmentRegex.Match(line);
                if (m.Success)
                {
                    var destAlias = m.Groups[1].Value;
                    var destReg = m.Groups[2].Value;
                    var offsetHex = m.Groups[3].Value;
                    var sourceAlias = m.Groups[4].Value;
                    var sourceReg = m.Groups[5].Value;

                    var dest = _registerAliases.FirstOrDefault(pair => pair.Value == destAlias);
                    var src = _registerAliases.FirstOrDefault(pair => pair.Value == sourceAlias);

                    var offset = int.Parse($"{offsetHex}", NumberStyles.HexNumber) - 16; //First 16 bytes appear to be reserved
                    if (offset >= 0)
                    {
                        try
                        {
                            _registerTypes.TryGetValue(destReg, out var destRegType);

                            var field = SharedState.AllTypeDefinitions.Find(t => t.FullName == destRegType?.FullName)?.Fields[offset / 8];

                            _registerTypes.TryGetValue(sourceReg, out var regType);

                            _methodFunctionality.Append($"\t\tSet field {field?.Name} (type {field?.FieldType.FullName}) of {dest.Value} to {src.Value} (type {regType?.FullName}) (on line {instructions.IndexOf(instruction)})\n");
                        }
                        catch
                        {
                            _methodFunctionality.Append($"\t\tSet field [#{offset / 8}?] of {dest.Value} to {src.Value} (on line {instructions.IndexOf(instruction)})\n");
                        }
                    }
                }

                typeDump.Append($"\t\t{line}");

                #region Argument Detection

                //Using a single operand (except for a PUSH operation) - check if the register, if there is one, is defined. 
                if (instruction.Operands.Length == 1 && instruction.Mnemonic != ud_mnemonic_code.UD_Ipush)
                {
                    //Is this a register?
                    if (instruction.Operands[0].Type == ud_type.UD_OP_REG)
                    {
                        //It is. Is it known to us?
                        var theBase = instruction.Operands[0].Base;
                        var register = theBase.ToString().Replace("UD_R_", "").ToLower();
                        if (!knownRegisters.Contains(register))
                        {
                            //No. Must be an argument then.
                            argumentRegisters.Add(register);
                            knownRegisters.Add(register);
                        }
                    }
                }
                else if (instruction.Operands.Length == 2)
                {
                    //Check the SECOND operand. Is it a register?
                    if (instruction.Operands[1].Type == ud_type.UD_OP_REG)
                    {
                        //Yes, check it.
                        var theBase = instruction.Operands[1].Base;
                        var register = theBase.ToString().Replace("UD_R_", "").ToLower();

                        //Special case, `XOR reg, reg` is generated to zero out a register, we should ignore it
                        if ((instruction.Mnemonic == ud_mnemonic_code.UD_Ixor || instruction.Mnemonic == ud_mnemonic_code.UD_Ixorps) && instruction.Operands[0].Type == instruction.Operands[1].Type && instruction.Operands[0].Base == instruction.Operands[1].Base)
                        {
                            knownRegisters.Add(register);
                            typeDump.Append($" ; zero out register {register}");
                        }

                        if (!knownRegisters.Contains(register))
                        {
                            //No. Must be an argument then.
                            typeDump.Append($" ; register {register} is used here without being assigned a value previously - must be passed into the function");

                            argumentRegisters.Add(register);
                            knownRegisters.Add(register);
                        }
                    } else if (instruction.Operands[1].Type == ud_type.UD_OP_MEM && instruction.Operands[1].Base != ud_type.UD_R_RIP)
                    {
                        //Check for field read
                    }

                    //And check if we're defining the register used in the first operand.
                    if (instruction.Mnemonic == ud_mnemonic_code.UD_Imov || instruction.Mnemonic == ud_mnemonic_code.UD_Imovaps || instruction.Mnemonic == ud_mnemonic_code.UD_Imovss || instruction.Mnemonic == ud_mnemonic_code.UD_Imovzx
                        || instruction.Mnemonic == ud_mnemonic_code.UD_Ilea)
                    {
                        if (instruction.Operands[0].Type == ud_type.UD_OP_REG)
                        {
                            //This register is now defined.
                            var theBase = instruction.Operands[0].Base;
                            var destReg = theBase.ToString().Replace("UD_R_", "").ToLower();
                            if (!knownRegisters.Contains(destReg))
                            {
                                typeDump.Append($" ; - Register {destReg} is first given a value here.");
                                knownRegisters.Add(destReg);
                            }

                            switch (instruction.Operands[1].Type)
                            {
                                case ud_type.UD_OP_REG:
                                    //reg
                                    theBase = instruction.Operands[1].Base;
                                    var sourceReg = theBase.ToString().Replace("UD_R_", "").ToLower();
                                    if (_registerAliases.ContainsKey(sourceReg))
                                    {
                                        _registerAliases[destReg] = _registerAliases[sourceReg];
                                        if (_registerTypes.ContainsKey(sourceReg))
                                            _registerTypes[destReg] = _registerTypes[sourceReg];
                                    }
                                    else if (_registerAliases.ContainsKey(destReg))
                                    {
                                        _registerAliases.Remove(destReg); //If we have one for the dest but not the source, clear it
                                        _registerTypes.Remove(destReg);
                                    }

                                    break;
                                case ud_type.UD_OP_MEM when instruction.Operands[1].Base == ud_type.UD_R_RIP:
                                    //[rip+0xyyyyy] is a global read
                                    var offset = Utils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]);
                                    if (offset == 0) break;
                                    var addr = _methodStart + offset;
                                    typeDump.Append($"; - Read on memory location 0x{addr:X}");
                                    var glob = _globals.Find(g => g.Offset == addr);
                                    if (glob.Offset == addr)
                                    {
                                        typeDump.Append($" - this is global value {glob.Name} of type {glob.IdentifierType}");
                                        _registerAliases[destReg] = $"global_{glob.IdentifierType}_{glob.Name}";
                                    }
                                    else
                                    {
                                        //Try to read literal
                                        try
                                        {
                                            var actualAddress = _cppAssembly.MapVirtualAddressToRaw(addr);
                                            typeDump.Append(" - might be in file at " + actualAddress);
                                            if (char.IsLetter(Convert.ToChar(_cppAssembly.raw[actualAddress])))
                                            {
                                                var literal = new StringBuilder();
                                                while (_cppAssembly.raw[actualAddress] != 0 && literal.Length < 250)
                                                {
                                                    literal.Append(Convert.ToChar(_cppAssembly.raw[actualAddress]));
                                                    actualAddress++;
                                                }

                                                typeDump.Append(" - literal: " + literal);
                                                _registerAliases[destReg] = literal.ToString();
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            //suppress
                                        }
                                    }

                                    break;
                            }
                        }
                    }
                }

                #endregion

                #region Jump Detection

                if (instruction.Mnemonic == ud_mnemonic_code.UD_Ijmp || instruction.Mnemonic == ud_mnemonic_code.UD_Icall)
                {
                    try
                    {
                        //JMP instruction, try find function
                        var jumpTarget = Utils.GetJumpTarget(instruction, _methodStart + instruction.PC);
                        typeDump.Append($" ; jump to 0x{jumpTarget:X}");

                        SharedState.MethodsByAddress.TryGetValue(jumpTarget, out var target);

                        TypeReference returnType = null;
                        if (target != null)
                        {
                            //Console.WriteLine("Found a function call!");
                            returnType = HandleFunctionCall(target);
                        }
                        else if (jumpTarget == _keyFunctionAddresses.AddrBailOutFunction)
                        {
                            typeDump.Append(" - this is the bailout function and will be ignored.");
                        }
                        else if (jumpTarget == _keyFunctionAddresses.AddrInitFunction)
                        {
                            typeDump.Append(" - this is the initialization function and will be ignored.");
                        }
                        else if (jumpTarget == _keyFunctionAddresses.AddrNewFunction)
                        {
                            typeDump.Append(" - this is the constructor function.");
                            var success = false;
                            _registerAliases.TryGetValue("rcx", out var glob);
                            if (glob != null)
                            {
                                var match = Regex.Match(glob, "global_([A-Z]+)_([^/]+)");
                                if (match != null && match.Success)
                                {
                                    Enum.TryParse<AssemblyBuilder.GlobalIdentifier.Type>(match.Groups[1].Value, out var type);
                                    var global = _globals.Find(g => g.Name == match.Groups[2].Value && g.IdentifierType == type);
                                    if (global.Offset != 0)
                                    {
                                        var (definedType, genericParams) = Utils.TryLookupTypeDefByName(global.Name);

                                        if (definedType != null)
                                        {
                                            _methodFunctionality.Append($"\t\tCreates an instance of type {definedType.FullName}{(genericParams.Length > 0 ? $" with generic parameters {string.Join(",", genericParams)}" : "")}\n");
                                            returnType = definedType;
                                            success = true;
                                        }
                                        else
                                        {
                                            _methodFunctionality.Append($"\t\tCreates an instance of (unresolved) type {global.Name}\n");
                                            success = true;
                                        }
                                    }
                                }
                            }

                            if (!success)
                                _methodFunctionality.Append("\t\tCreates an instance of [something]\n");
                        }
                        else if (jumpTarget == _keyFunctionAddresses.AddrInitStaticFunction)
                        {
                            typeDump.Append(" - this is the static class initializer and will be ignored");
                        } else if (jumpTarget == _keyFunctionAddresses.AddrNativeLookup)
                        {
                            typeDump.Append(" - this is the native lookup function");
                            _registerAliases.TryGetValue("rcx", out var functionName);
                            //TODO Attempt to resolve. Also, this will be followed by a CALL rax, which currently throws an InvalidOperationException but should instead lookup this function and call it.
                            if (functionName != null)
                            {
                                _methodFunctionality.Append($"\t\tLooks up native function {functionName}\n");
                            }
                        } else if (jumpTarget == _keyFunctionAddresses.AddrNativeLookupGenMissingMethod)
                        {
                            typeDump.Append(" - this is the native lookup bailout function");
                        }
                        else
                        {
                            //Is this somewhere in this function?
                            var methodEnd = _methodStart + (ulong) instructions.Count;
                            if (_methodStart <= jumpTarget && jumpTarget <= methodEnd)
                            {
                                var pos = jumpTarget - _methodStart;
                                typeDump.Append($" - offset 0x{pos:X} in this function");
                            }
                            else
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
                                        if (alias != null && alias.StartsWith("global_METHOD"))
                                        {
                                            //Success! Now we want the method ref
                                            var methodFullName = alias.Replace("global_METHOD_", "").Replace("_" + register, "");
                                            var split = methodFullName.Split(new[] {'.'}, 999, StringSplitOptions.None).ToList();
                                            var methodName = split.Last();
                                            methodName = methodName.Substring(0, methodName.IndexOf("(", StringComparison.Ordinal));
                                            split.RemoveAt(split.Count - 1);
                                            var typeName = string.Join(".", split);


                                            if (typeName.Count(c => c == '<') > 1) //Clean up double type param
                                                if (Regex.Match(typeName, "([^#]+)<\\w+>(<[^#]+>)") is Match match && match != null && match.Success)
                                                    typeName = match.Groups[1].Value + match.Groups[2].Value;

                                            // _methodFunctionality.Append($"\t\tBelieved Override Method Call Located: Type name {typeName}, method name {methodName}\n");

                                            // if(typeName.StartsWith("List"))
                                            //     Console.WriteLine("List");

                                            var (definedType, genericParams) = Utils.TryLookupTypeDefByName(typeName);
                                            var method = definedType?.Methods?.First(methd => methd.Name.Split('.').Last() == methodName);

                                            if (method != null)
                                            {
                                                var requiredCount = (method.IsStatic ? 0 : 1) + method.Parameters.Count;
                                                // _methodFunctionality.Append($"\t\tConfirmed: Method is {method.FullName}, generic params {string.Join(",", genericParams)} Required parameter count is {method.Parameters.Count}, provided with {providedParamCount}\n");
                                                if (requiredCount == providedParamCount)
                                                {
                                                    returnType = HandleFunctionCall(method);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (instruction.Mnemonic == ud_mnemonic_code.UD_Icall && returnType != null && returnType.Name != "Void")
                        {
                            _registerTypes["rax"] = returnType;
                            _registerAliases["rax"] = $"local{localNum}";
                            _methodFunctionality.Append($"\t\tCreates local variable local{localNum} of type {returnType} and sets it to the return value\n");
                            localNum++;
                        }
                    }
                    catch (Exception e)
                    {
                        typeDump.Append($" ; {e.GetType()} thrown trying to locate JMP target.");
                    }
                }

                #endregion

                typeDump.Append("\n");
            }


            if (argumentRegisters.Count > 0)
            {
                typeDump.Append("Method Arguments Identified: \n");
                foreach (var registerName in argumentRegisters)
                {
                    typeDump.Append($"\t{registerName}\n");
                }
            }

            typeDump.Append($"\n\tMethod Synopsis:\n{_methodFunctionality}\n");
        }

        private TypeReference HandleFunctionCall(MethodDefinition target)
        {
            _typeDump.Append($" - function {target.FullName}");
            _methodFunctionality.Append($"\t\tCalls {(target.IsStatic ? "static" : "instance")} function {target.FullName}");
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
                    args.Add($"{_registerAliases[possibility]} as this in register {possibility}");
                    break;
                }
            }

            foreach (var parameter in target.Parameters)
            {
                var possibilities = paramRegisters.First().Split('/');
                paramRegisters.RemoveAt(0);
                var success = false;
                foreach (var possibility in possibilities)
                {
                    if (!_registerAliases.ContainsKey(possibility)) continue;
                    args.Add($"{_registerAliases[possibility]} as {parameter.Name} in register {possibility}");
                    success = true;
                    break;
                }

                if (!success)
                    args.Add($"<unknown> as {parameter.Name} in one of the registers {string.Join("/", possibilities)}");
            }

            if (args.Count > 0)
                _methodFunctionality.Append($" with parameters: {string.Join(", ", args)}");

            _methodFunctionality.Append("\n");
            return target.ReturnType;
        }

        private List<string> DetectPotentialLoops(List<Instruction> instructions)
        {
            return instructions
                .Where(instruction => instruction.Mnemonic == ud_mnemonic_code.UD_Iinc)
                .Select(instruction => instruction.Operands[0].Base.ToString().Replace("UD_R_", "").ToLower())
                .Distinct()
                .ToList();
        }
    }
}