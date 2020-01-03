using System;
using System.Collections.Generic;
using System.Globalization;
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

                if (Utils.CheckForInitCallAtIndex(_methodStart, instructions, i, _keyFunctionAddresses))
                {
                    //Need to preserve the MOV which is the value after this one.
                    ret.Add(instructions[i + 1]);
                    
                    //And skip 5 instructions
                    i += 5;
                    continue;
                }

                var insn = instructions[i];

                try
                {
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
            //As we're on windows, function params are passed RCX RDX R8 R9, then the stack
            //If these are floating point numbers, they're put in XMM0 to 3
            //Register eax/rax/whatever you want to call it is the return value (both of any functions called in this one and this function itself)
            
            /*
             * TODO: Notes for whenever I next pick this up (hopefully tomorrow): We need a way to locate the generic method call helper functions (e.g. List#get_Item is called with a helper function, using a global reference to the method, which
             * TODO: should be in the globals param to this function. Also, there's a function that looks up a natively-implemented function by name (used in DebugDraw#LateUpdate) that is needed for clean decompilation.
             * TODO: Basically, we need a preprocessing task that locates the addresses of certain key functions using known calls to them (for example, the generic method one is at least used in Unity internal code, if not anywhere in the CLR)
             */

            typeDump.Append($"Method: {_methodDefinition.FullName}: (");
            
            var methodFunctionality = new StringBuilder();

            //Pass 0: Disassemble
            var instructions = Utils.DisassembleBytes(_method.MethodBytes);

            //Pass 1: Removal of unneeded generated code
            instructions = TrimOutIl2CppCrap(instructions);
            
            //Pass 2: Loop Detection
            var loopRegisters = DetectPotentialLoops(instructions);

            if(loopRegisters.Count > 0)
                methodFunctionality.Append($"\t\tPotential Loops Centred on Register(s): {string.Join(",", loopRegisters)}\n");

            var distinctMnemonics = new List<ud_mnemonic_code>(instructions.Select(i => i.Mnemonic).Distinct());
            typeDump.Append($"uses {distinctMnemonics.Count} unique operations)\n");
            allUsedMnemonics = new List<ud_mnemonic_code>(allUsedMnemonics.Concat(distinctMnemonics).Distinct());

            var registerAliases = new Dictionary<string, string>(); //Reg name to alias.

            //Dump params
            typeDump.Append("\tParameters in registers: \n");

            var registers = new List<string>(new[] {"rcx/xmm0", "rdx/xmm1", "r8/xmm2", "r9/xmm3"});
            var stackIndex = 0;

            if (!_methodDefinition.IsStatic)
            {
                var pos = registers[0];
                registers.RemoveAt(0);
                typeDump.Append($"\t\t<This> this in register {pos}\n");
                foreach (var reg in pos.Split('/'))
                {
                    registerAliases[reg] = "this";
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
                        registerAliases[reg] = parameter.Name;
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

            var fieldAssignmentRegex = new Regex(@"\[(\w+)_(?:\w+)\+0x(\d+)\], (\w+)_(?:\w+)");

            foreach (var instruction in instructions)
            {
                //Preprocessing to make it easier to read.
                var line = instruction.ToString();
                line = registerAliases.Aggregate(line, (current, kvp) => current.Replace(kvp.Key, $"{kvp.Value}_{kvp.Key}"));

                var m = fieldAssignmentRegex.Match(line);
                if (m.Success)
                {
                    var destAlias = m.Groups[1].Value;
                    var offsetHex = m.Groups[2].Value;
                    var sourceAlias = m.Groups[3].Value;

                    var dest = registerAliases.FirstOrDefault(pair => pair.Value == destAlias);
                    var src = registerAliases.FirstOrDefault(pair => pair.Value == sourceAlias);

                    var offset = int.Parse($"{offsetHex}", NumberStyles.HexNumber) - 24; //First 16 bytes appear to be reserved
                    if (offset >= 0)
                    {
                        try
                        {
                            var field = _methodDefinition.DeclaringType.Fields[offset / 8];

                            methodFunctionality.Append($"\t\tSet field {field.Name} of {dest.Value} to {src.Value} (on line {instructions.IndexOf(instruction)})\n");
                        }
                        catch
                        {
                            methodFunctionality.Append($"\t\tSet field [#{offset / 8}?] of {dest.Value} to {src.Value} (on line {instructions.IndexOf(instruction)})\n");
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
                        if (instruction.Mnemonic == ud_mnemonic_code.UD_Ixor && instruction.Operands[0].Type == instruction.Operands[1].Type && instruction.Operands[0].Base == instruction.Operands[1].Base)
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
                    }

                    //And check if we're defining the register used in the first operand.
                    if (instruction.Mnemonic == ud_mnemonic_code.UD_Imov || instruction.Mnemonic == ud_mnemonic_code.UD_Imovaps || instruction.Mnemonic == ud_mnemonic_code.UD_Imovss)
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

                            theBase = instruction.Operands[1].Base;
                            var sourceReg = theBase.ToString().Replace("UD_R_", "").ToLower();
                            if (registerAliases.ContainsKey(sourceReg))
                                registerAliases[destReg] = registerAliases[sourceReg];
                            else if (registerAliases.ContainsKey(destReg))
                                registerAliases.Remove(destReg); //If we have one for the dest but not the source, clear it
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

                        if (target != null)
                        {
                            //Console.WriteLine("Found a function call!");
                            typeDump.Append($" - function {target.FullName}");
                            methodFunctionality.Append($"\t\tCalls function {target.FullName}\n");
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
                            //TODO: Look at what's in register rcx as that's what's being created.
                            methodFunctionality.Append("\t\tCreates an instance of [something]\n");
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

            typeDump.Append($"\n\tMethod Synopsis:\n{methodFunctionality}\n");
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