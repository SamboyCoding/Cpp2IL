using System.Collections.Generic;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Utils;

// integer args in X0-X7, fp args in V0-V7 (independent counters), rest on the stack.
// Oversized struct returns go via a pointer in X8, which is not an argument register.
public class Arm64CallingConventionResolver : BaseCallingConventionResolver
{
    private const int PtrSize = 8;

    private static readonly string[] IntegerRegisters = ["X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7"];
    private static readonly string[] FloatRegisters = ["V0", "V1", "V2", "V3", "V4", "V5", "V6", "V7"];

    public override Register ReturnRegister(MethodAnalysisContext ctx)
        => new(null, IsFloatingPoint(ctx.ReturnType) ? "V0" : "X0");

    public override Register? HiddenReturnBufferRegister(MethodAnalysisContext ctx)
        => ReturnsViaHiddenBuffer(ctx) ? new Register(null, "X8") : null;

    public override bool ReturnsViaHiddenBuffer(MethodAnalysisContext ctx)
    {
        if (ctx.IsVoid)
            return false;

        var returnType = ctx.ReturnType;
        if (!returnType.IsValueType || IsFloatingPoint(returnType))
            return false;

        var size = TypeSizes.UnboxedSize(returnType, PtrSize);
        if (size == 0)
            return false; // unknown size (e.g. generic), assume a register return

        return size > 16;
    }

    protected override (string[] Integer, string[] Float) RawRegisters(ApplicationAnalysisContext app)
        => (IntegerRegisters, FloatRegisters);

    /// <summary>
    /// A homogeneous float aggregate takes one V register per element, so a <c>Vector3</c> argument
    /// occupies V0..V2 and the arguments after it are shifted by two registers. Emitting a single
    /// integer slot for it makes every following argument read from the wrong register.
    /// </summary>
    protected override void AddParameterSlots(List<(bool IsFloat, bool Emit)> slots, TypeAnalysisContext? type, bool emit)
    {
        var elements = type == null ? 0 : HomogeneousFloatAggregate.Count(type);

        if (elements == 0)
        {
            base.AddParameterSlots(slots, type, emit);
            return;
        }

        for (var i = 0; i < elements; i++)
            slots.Add((true, emit && i == 0));
    }

    protected override bool HiddenBufferConsumesArgumentSlot => false;

    public override IOperand[] ResolveForManaged(MethodAnalysisContext ctx)
    {
        var args = new List<IOperand>();

        var integer = 0;
        var floating = 0;
        var stack = 0;

        void AddParameter(TypeAnalysisContext? type)
        {
            var slots = new List<(bool IsFloat, bool Emit)>();
            AddParameterSlots(slots, type, true);

            Register? first = null;

            foreach (var (isFloat, _) in slots)
            {
                // The counter must advance for every slot of the aggregate, not only the one that
                // contributes the operand - `??=` would short-circuit the increment away.
                if (isFloat)
                {
                    if (floating >= FloatRegisters.Length)
                    {
                        first = null;
                        break;
                    }

                    var name = FloatRegisters[floating++];
                    first ??= new Register(null, name);
                }
                else
                {
                    if (integer >= IntegerRegisters.Length)
                    {
                        first = null;
                        break;
                    }

                    var name = IntegerRegisters[integer++];
                    first ??= new Register(null, name);
                }
            }

            if (first != null)
            {
                args.Add(first);
                return;
            }

            args.Add(new StackOffset(stack));
            stack += PtrSize;
        }

        if (!ctx.IsStatic)
            AddParameter(null);

        foreach (var par in ctx.Parameters)
            AddParameter(par.ParameterType);

        AddParameter(null); // The MethodInfo argument

        return args.ToArray();
    }
}
