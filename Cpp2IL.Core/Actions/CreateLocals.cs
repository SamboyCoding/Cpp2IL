using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Actions;

public class CreateLocals : IAction
{
    public void Apply(MethodAnalysisContext method)
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
        if (!method.IsStatic)
        {
            var thisOperand = (Register)method.ParameterOperands[0];
            var thisLocal = method.Locals.First(l => l.Register.Number == thisOperand.Number && l.Register.Version == -1);

            thisLocal.Name = "this";
            thisLocal.IsThis = true;
            paramLocals.Add(thisLocal);
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
}
