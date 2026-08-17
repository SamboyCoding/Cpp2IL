using System.Collections.Generic;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlInstructionSet
{
    /// <summary>
    /// Get the raw method body for the given method.
    /// </summary>
    /// <param name="context">The method to get the body for</param>
    /// <param name="isAttributeGenerator">True if this is an attribute generator function, false if it's a managed method</param>
    /// <returns>A byte array representing the method's body</returns>
    public abstract BinarySlice GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator);

    /// <summary>
    /// Returns the virtual address from which the given method starts. By default, returns the <see cref="Il2CppMethodDefinition.MethodPointer"/> property, but
    /// can be overridden to provide a different value for instruction sets where this is necessary, for example WASM.
    /// </summary>
    /// <param name="context">The analysis context for the method to return the pointer for.</param>
    /// <returns></returns>
    public virtual ulong GetPointerForMethod(MethodAnalysisContext context) => context.UnderlyingPointer;

    /// <summary>
    /// Returns the ISIL representation of the given method. You should convert all native machine code instructions to their equivalent
    /// ISIL form, and then return the resulting instruction list. From there, a control flow graph will be built and the method will be
    /// analyzed.
    /// </summary>
    /// <param name="context">The method to convert to ISIL</param>
    /// <returns>An array of <see cref="Instruction"/> structs representing the functionality of this method in an instruction-set-independent manner.</returns>
    public abstract List<Instruction> GetIsilFromMethod(MethodAnalysisContext context);

    /// <summary>
    /// Returns operands that represent the parameters.
    /// </summary>
    /// <param name="context">The method to get parameters from.</param>
    /// <returns>A list of parameter operands.</returns>
    public abstract List<IOperand> GetParameterOperandsFromMethod(MethodAnalysisContext context);

    /// <summary>
    /// Create and populate a BaseKeyFunctionAddresses object which can then be populated.
    /// </summary>
    /// <returns>A subclass of <see cref="BaseKeyFunctionAddresses"/> specific to this instruction set</returns>
    public abstract BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance();

    /// <summary>
    /// The calling convention model for this instruction set, used by analysis passes to reason about call
    /// arguments and return values. Null if this instruction set does not model calling conventions.
    /// </summary>
    public virtual BaseCallingConventionResolver? CallingConventionResolver => null;

    /// <summary>
    /// For the function at <paramref name="address"/>, returns the absolute
    /// addresses it references as constant data (candidate C-string pointers, e.g. an exception type name)
    /// and the addresses it calls (so a chain of helpers can be followed).
    /// </summary>
    public virtual (IReadOnlyList<ulong> DataReferences, IReadOnlyList<ulong> CallTargets) InspectPotentialThrowHelper(ApplicationAnalysisContext context, ulong address)
        => ([], []);

    /// <summary>
    /// For a simple thunk function, returns the jump target. Returns 0 if the function at <paramref name="thunkAddress"/>
    /// is not such a thunk, or if this instruction set does not implement thunk following.
    /// </summary>
    public virtual ulong GetThunkTarget(ApplicationAnalysisContext context, ulong thunkAddress) => 0;

    /// <summary>
    /// For an internal call, returns the address of the runtime function it tail-calls into.
    /// Unlike a thunk these can do a small amount of work before the jump, so implementations must scan the whole body, unlike for <see cref="GetThunkTarget"/>.
    /// Returns 0 if there isn't exactly one such target, or if this instruction set does not implement it.
    /// </summary>
    public virtual ulong GetInternalCallTarget(MethodAnalysisContext method) => 0;

    /// <summary>
    /// Create a string containing the raw native disassembly of the given method. You should print one instruction per line, alongside its address if applicable and available.
    /// </summary>
    /// <param name="context">The method context to disassemble.</param>
    /// <returns>A string containing one instruction per line.</returns>
    public abstract string PrintAssembly(MethodAnalysisContext context);
}
