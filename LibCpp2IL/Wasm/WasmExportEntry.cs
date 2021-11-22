namespace LibCpp2IL.Wasm
{
    public class WasmExportEntry
    {
        public WasmString Name;
        public WasmExternalKind Kind;
        public ulong Index;

        public WasmExportEntry(WasmFile file)
        {
            Name = new(file);
            Kind = (WasmExternalKind) file.ReadByte();
            Index = file.BaseStream.ReadLEB128Unsigned();
        }
    }
}