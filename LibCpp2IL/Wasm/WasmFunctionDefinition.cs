namespace LibCpp2IL.Wasm
{
    public class WasmFunctionDefinition
    {
        public bool IsImport;
        public string? ImportName;
        public ulong Pointer;
        public WasmFunctionBody? AssociatedFunctionBody;
        private ulong TypeIndex;

        public WasmFunctionDefinition(WasmImportEntry entry)
        {
            IsImport = true;
            TypeIndex = entry.FunctionEntry;
            ImportName = entry.Module + "." + entry.Field;
        }

        public WasmFunctionDefinition(WasmFile file, WasmFunctionBody body, int index)
        {
            IsImport = false;
            Pointer = (ulong) body.InstructionsOffset;
            TypeIndex = file.FunctionSection.Types[index];
            AssociatedFunctionBody = body;
        }

        public WasmTypeEntry GetType(WasmFile file) => file.TypeSection.Types[(int) TypeIndex];
    }
}