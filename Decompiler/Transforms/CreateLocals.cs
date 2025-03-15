using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Replaces registers with local variables.
/// </summary>
public class CreateLocals : ITransform
{
    public void Apply(Method method)
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
        var workList = new Queue<Instruction>(method.Instructions);

        while (workList.Count > 0)
        {
            var instruction = workList.Dequeue();

            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                // Nested instruction
                if (operand is Instruction instructionOp)
                    workList.Enqueue(instructionOp);

                // Register
                if (operand is not Register register) continue;
                instruction.Operands[i] = locals[register];
            }
        }

        method.Locals = locals.Select(kv => kv.Value).ToList();

        // Add parameter names
        var returnRegister = method.GetReturnLocal();
        var paramLocals = new List<LocalVariable>();

        foreach (var local in method.Locals)
        {
            // Return value
            if (returnRegister != null && local.Register == returnRegister.Register)
            {
                local.Name = "return";
                continue;
            }

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
                local.Name = method.GetParameterName(paramIndex + (method.Definition.IsStatic ? 0 : -1)); // -1 for 'this' param
                paramLocals.Add(local);
            }
        }

        method.ParameterLocals = paramLocals;
    }

    private static List<Register> GetRegisters(Instruction instruction)
    {
        // Get all registers
        var registers = new List<Register>();
        foreach (var operand in instruction.Operands)
        {
            // Nested instruction
            if (operand is Instruction instructionOp)
                registers.AddRange(GetRegisters(instructionOp));

            // Register
            if (operand is not Register register) continue;

            if (!registers.Contains(register))
                registers.Add(register);
        }

        return registers;
    }
}
