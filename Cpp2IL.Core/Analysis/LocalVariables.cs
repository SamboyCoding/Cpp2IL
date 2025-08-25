using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

public static class LocalVariables
{
    public static int MaxTypePropagationLoopCount = 5000;

    public static void CreateAll(MethodAnalysisContext method)
    {
        var cfg = method.ControlFlowGraph!;

        // Get all registers
        var registers = new List<Register>();
        foreach (var instruction in cfg.Instructions)
            registers.AddRange(GetRegisters(instruction));

        // Remove duplicates
        registers = registers.Distinct().ToList();

        // Map those to locals
        var locals = new Dictionary<Register, LocalVariable>();
        for (var i = 0; i < registers.Count; i++)
        {
            var register = registers[i];
            locals.Add(register, new LocalVariable($"v{i}", register));
        }

        // Replace registers with locals
        foreach (var instruction in cfg.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is Register register)
                    instruction.Operands[i] = locals[register];

                if (operand is MemoryOperand memory)
                {
                    if (memory.Base != null)
                    {
                        var baseRegister = (Register)memory.Base;
                        memory.Base = locals[baseRegister];
                    }

                    if (memory.Index != null)
                    {
                        var index = (Register)memory.Index;
                        memory.Index = locals[index];
                    }

                    instruction.Operands[i] = memory;
                }
            }
        }

        method.Locals = locals.Select(kv => kv.Value).ToList();

        // Return local names
        var retValIndex = 0;
        for (var i = 0; i < cfg.Instructions.Count; i++)
        {
            var instruction = cfg.Instructions[i];
            if (instruction.OpCode != OpCode.Return || instruction.Operands.Count != 1) continue;

            var returnLocal = (LocalVariable)instruction.Sources[0];

            returnLocal.Name = $"returnVal{retValIndex + 1}";
            returnLocal.IsReturn = true;
            retValIndex++;
        }

        // Add parameter names
        var paramLocals = new List<LocalVariable>();

        var operandOffset = method.IsStatic ? 0 : 1; // 'this'

        // 'this' param
        if (!method.IsStatic && method.Locals.Count > 0)
        {
            var thisOperand = (Register)method.ParameterOperands[0];
            var thisLocal = method.Locals.FirstOrDefault(l => l.Register.Number == thisOperand.Number && l.Register.Version == -1);

            if (thisLocal != null)
            {
                thisLocal.Name = "this";
                thisLocal.IsThis = true;
                paramLocals.Add(thisLocal);
            }
            else
            {
                method.AddWarning($"'this' local not found (operand: {thisOperand})");
            }
        }

        // Check if method has MethodInfo*
        var hasMethodInfo = (method.ParameterOperands.Count - operandOffset) > method.Parameters.Count;
        var methodInfoIndex = method.ParameterOperands.Count - 1;

        // Add normal parameter names
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            var operandIndex = i + operandOffset;
            if (hasMethodInfo && operandIndex == methodInfoIndex)
                break; // Skip MethodInfo*

            if (operandIndex >= method.ParameterOperands.Count)
                break;

            if (method.ParameterOperands[operandIndex] is not Register reg)
                continue;

            var local = method.Locals.FirstOrDefault(l => l.Register.Number == reg.Number && l.Register.Version == -1);
            if (local == null)
                continue;

            local.Name = method.Parameters[i].ParameterName;
            paramLocals.Add(local);
        }

        // Add MethodInfo*
        if (hasMethodInfo)
        {
            var methodInfoOperand = (Register)method.ParameterOperands[methodInfoIndex];
            var methodInfoLocal = method.Locals.FirstOrDefault(l => l.Register.Number == methodInfoOperand.Number && l.Register.Version == -1);

            if (methodInfoLocal != null)
            {
                methodInfoLocal.Name = "methodInfo";
                methodInfoLocal.IsMethodInfo = true;
                paramLocals.Add(methodInfoLocal);
            }
        }

        method.ParameterLocals = paramLocals;
    }

    public static void RemoveUnused(MethodAnalysisContext method)
    {
        var cfg = method.ControlFlowGraph!;
        cfg.BuildUseDefLists();

        for (var i = 0; i < method.Locals.Count; i++)
        {
            var local = method.Locals[i];

            if (cfg.Blocks.Any(b => b.Use.Contains(local) || b.Def.Contains(local)))
                continue;

            method.Locals.Remove(local);
            i--;
        }
    }

    private static List<Register> GetRegisters(Instruction instruction)
    {
        var registers = new List<Register>();

        foreach (var operand in instruction.Operands)
        {
            if (operand is Register register)
            {
                if (!registers.Contains(register))
                    registers.Add(register);
            }

            if (operand is MemoryOperand memory)
            {
                if (memory.Base != null)
                {
                    var baseRegister = (Register)memory.Base;
                    if (!registers.Contains(baseRegister))
                        registers.Add(baseRegister);
                }

                if (memory.Index != null)
                {
                    var index = (Register)memory.Index;
                    if (!registers.Contains(index))
                        registers.Add(index);
                }
            }
        }

        return registers;
    }

    public static void PropagateTypes(MethodAnalysisContext method)
    {
        PropagateFromReturn(method);
        PropagateFromParameters(method);
        PropagateFromCallParameters(method);
        PropagateThroughMoves(method);
    }

    private static void PropagateThroughMoves(MethodAnalysisContext method)
    {
        var changed = true;
        var loopCount = 0;

        while (changed)
        {
            changed = false;
            loopCount++;

            if (MaxTypePropagationLoopCount != -1 && loopCount > MaxTypePropagationLoopCount)
                throw new DecompilerException($"Type propagation through moves not settling! (looped {MaxTypePropagationLoopCount} times)");

            foreach (var instruction in method.ControlFlowGraph!.Instructions)
            {
                if (instruction.OpCode != OpCode.Move)
                    continue;

                if (instruction.Operands[0] is LocalVariable destination && instruction.Operands[1] is LocalVariable source)
                {
                    // Move ??, local
                    if (destination.Type == null && source.Type != null)
                    {
                        destination.Type = source.Type;
                        changed = true;
                    }
                    // Move local, ??
                    else if (source.Type == null && destination.Type != null)
                    {
                        source.Type = destination.Type;
                        changed = true;
                    }
                }

                if (instruction.Operands[0] is LocalVariable destination2 && instruction.Operands[1] is TypeAnalysisContext source2)
                {
                    // Move ??, type
                    if (destination2.Type == null)
                    {
                        destination2.Type = source2;
                        changed = true;
                    }
                }
            }
        }
    }

    private static void PropagateFromCallParameters(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (!instruction.IsCall)
                continue;

            if (instruction.Operands[0] is not MethodAnalysisContext calledMethod)
                continue;

            // Constructor, set return variable type
            if (calledMethod.Name == ".ctor" || calledMethod.Name == ".cctor")
            {
                if (instruction.Destination is LocalVariable constructorReturn)
                {
                    constructorReturn.Type = calledMethod.DeclaringType;
                    continue;
                }
            }
            else // Return value
            {
                if (instruction.Destination is LocalVariable returnValue)
                    returnValue.Type = calledMethod.ReturnType;
            }

            // 'this' param
            if (!calledMethod.IsStatic)
            {
                if (instruction.Operands[instruction.OpCode == OpCode.CallVoid ? 1 : 2] is LocalVariable thisParam)
                    thisParam.Type = calledMethod.DeclaringType;
            }

            // Set types
            var paramOffset = calledMethod.IsStatic ? 1 : 2;
            if (instruction.OpCode == OpCode.Call) // Skip return value
                paramOffset += 1;

            for (var i = paramOffset; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is LocalVariable local)
                {
                    if ((i - paramOffset) > calledMethod.Parameters.Count - 1) // Probably MethodInfo*
                        continue;

                    local.Type = calledMethod.Parameters[i - paramOffset].ParameterType;
                }
            }
        }
    }

    private static void PropagateFromParameters(MethodAnalysisContext method)
    {
        if (method.Parameters.Count == 0)
            return;

        // 'this'
        if (!method.IsStatic)
        {
            var thisLocal = method.ParameterLocals.FirstOrDefault(p => p.IsThis);
            if (thisLocal != null)
                thisLocal.Type = method.DeclaringType;
        }

        // Normal params
        var paramIndex = 0;
        foreach (var local in method.ParameterLocals)
        {
            if (local.IsThis || local.IsMethodInfo)
                continue;

            if (paramIndex >= method.Parameters.Count)
                break;

            local.Type = method.Parameters[paramIndex].ParameterType;
            paramIndex++;
        }
    }

    private static void PropagateFromReturn(MethodAnalysisContext method)
    {
        var returns = method.ControlFlowGraph!.Instructions.Where(i => i.OpCode == OpCode.Return);

        foreach (var instruction in returns)
        {
            if (instruction.Operands.Count == 1 && instruction.Operands[0] is LocalVariable local)
                local.Type = method.ReturnType;
        }
    }
}
