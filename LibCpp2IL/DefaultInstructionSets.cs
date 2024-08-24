namespace LibCpp2IL;

public static class DefaultInstructionSets
{
    public static readonly InstructionSetId X86_64 = new("x86_64");
    public static readonly InstructionSetId X86_32 = new("x86_32");
    public static readonly InstructionSetId ARM_V7 = new("ArmV7");
    public static readonly InstructionSetId ARM_V8 = new("ArmV8");
    public static readonly InstructionSetId WASM = new("WASM");
}
