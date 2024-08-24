namespace Cpp2IL.Plugin.BuildReport.Model;

public class OutputBinaryData
{
    public OutputMethodData[] Methods { get; set; } = Array.Empty<OutputMethodData>();
    public OutputReadableClassData[] RawReadableClasses { get; set; } = Array.Empty<OutputReadableClassData>();
}
