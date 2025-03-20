using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Replaces registers with local variables.
/// </summary>
public class CreateLocals : ITransform
{
    public void Apply(Method method, IContext context)
    {
        // Get all registers
        var registers = new List<Register>();
        foreach (var instruction in method.Instructions)
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
        foreach (var instruction in method.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is Register register)
                    instruction.Operands[i] = locals[register];

                if (operand is MemoryAddress memory)
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
        for (var i = 0; i < method.Instructions.Count; i++)
        {
            var instruction = method.Instructions[i];
            if (instruction.OpCode != OpCode.Return) continue;

            var returnLocal = (LocalVariable)instruction.Sources[0];

            returnLocal.Name = $"returnVal{i}";
        }

        // Add parameter names
        var paramLocals = new List<LocalVariable>();

        foreach (var local in method.Locals)
        {
            // Get param index of the local
            var paramIndex = method.Parameters.FindIndex(p => p is Register r && r.Number == local.Register.Number && local.Register.Version == -1);
            if (paramIndex == -1) continue;

            // this param
            if (paramIndex == 0 && !method.Definition.IsStatic)
            {
                local.Name = "this";
                paramLocals.Add(local);
                local.IsThis = true;
            }
            else
            {
                // Set the name
                var index = paramIndex + (method.Definition.IsStatic ? 0 : 1); // +1 to skip 'this' param

                if ((index > method.Definition.Parameters.Count - 1) || index == -1)
                    continue;

                local.Name = method.Definition.Parameters[index].Name;
                paramLocals.Add(local);
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

            if (operand is MemoryAddress memory)
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
