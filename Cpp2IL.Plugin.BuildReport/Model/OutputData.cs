namespace Cpp2IL.Plugin.BuildReport.Model;

public class OutputData
{
    public OutputBinaryData Binary { get; set; } = new();
    public OutputGlobalMetadataData GlobalMetadata { get; set; } = new();
}
