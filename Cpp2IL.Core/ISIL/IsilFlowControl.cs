namespace Cpp2IL.Core.ISIL;

public enum IsilFlowControl
{
    // Goto
    UnconditionalJump,

    // JumpIfEqual etc.
    ConditionalJump,

    // Switch
    IndexedJump,

    // Call
    MethodCall,

    // Return
    MethodReturn,

    // Interrupt
    Interrupt,

    // Add, Sub etc.
    Continue,
}
