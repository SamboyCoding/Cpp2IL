namespace LibCpp2IL.Wasm;

public class WasmFunctionDefinition
{
    public bool IsImport;
    public string? ImportName;
    public ulong Pointer;
    public WasmFunctionBody? AssociatedFunctionBody;
    private ulong TypeIndex;
    public int FunctionTableIndex; //Only valid for non-imported functions

    public WasmFunctionDefinition(WasmImportEntry entry)
    {
        IsImport = true;
        TypeIndex = entry.FunctionEntry;
        ImportName = entry.Module + "." + entry.Field;
    }

    public WasmFunctionDefinition(WasmFile file, WasmFunctionBody body, int index, int functionTableIndex)
    {
        FunctionTableIndex = functionTableIndex;
        IsImport = false;
        Pointer = (ulong)body.InstructionsOffset;
        TypeIndex = file.FunctionSection.Types[index];
        AssociatedFunctionBody = body;
    }

    public WasmTypeEntry GetType(WasmFile file) => file.TypeSection.Types[(int)TypeIndex];

    public override string ToString()
    {
        if (IsImport)
            return $"WASM Imported Function: {ImportName}, Pointer = {Pointer}";

        return $"WASM Function at pointer 0x{Pointer:X}, TypeIndex {TypeIndex}, with {AssociatedFunctionBody!.Instructions.Length} bytes of code";
    }
}
