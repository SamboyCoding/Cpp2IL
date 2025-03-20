using AsmResolver.DotNet;

namespace Cpp2IL.Decompiler;

/// <summary>
/// Provides additional context for the decompiler.
/// </summary>
public interface IContext
{
    /// <summary>
    /// Gets a type by its address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The type or null if not found.</returns>
    TypeDefinition? GetTypeByAddress(ulong address);

    /// <summary>
    /// Gets a field by its offset.
    /// </summary>
    /// <param name="type">Type that contains the field.</param>
    /// <param name="offset">Field offset.</param>
    /// <returns></returns>
    FieldDefinition? GetFieldByOffset(TypeDefinition type, long offset);
}
