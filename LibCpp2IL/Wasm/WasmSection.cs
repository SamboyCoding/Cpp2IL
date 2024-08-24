using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm;

public class WasmSection
{
    public WasmSectionId Type;
    public long Pointer;
    public ulong Size;

    protected WasmSection(WasmSectionId type, long pointer, ulong size)
    {
        Type = type;
        Pointer = pointer;
        Size = size;
    }

    public static WasmSection MakeSection(WasmFile file)
    {
        var pos = file.Position;
        var id = (WasmSectionId)file.ReadByte();
        var size = file.BaseStream.ReadLEB128Unsigned();
        LibLogger.VerboseNewline($"\t\tFound section of type {id} at 0x{pos:X} with length 0x{size:X}");

        size += (ulong)(file.Position - pos); //id and size are not included in the size

        return id switch
        {
            WasmSectionId.SEC_TYPE => new WasmTypeSection(id, pos, size, file),
            WasmSectionId.SEC_IMPORT => new WasmImportSection(id, pos, size, file),
            WasmSectionId.SEC_DATA => new WasmDataSection(id, pos, size, file),
            WasmSectionId.SEC_CODE => new WasmCodeSection(id, pos, size, file),
            WasmSectionId.SEC_FUNCTION => new WasmFunctionSection(id, pos, size, file),
            WasmSectionId.SEC_TABLE => new WasmTableSection(id, pos, size, file),
            WasmSectionId.SEC_GLOBAL => new WasmGlobalSection(id, pos, size, file),
            WasmSectionId.SEC_ELEMENT => new WasmElementSection(id, pos, size, file),
            WasmSectionId.SEC_EXPORT => new WasmExportSection(id, pos, size, file),
            _ => new WasmSection(id, pos, size)
        };
    }
}
