using System.Collections.Generic;

namespace Cpp2IL.Gui;

public static class MethodBodyModes
{
    public static readonly List<MethodBodyMode> AllModes = new()
    {
        MethodBodyMode.Stubs,
        MethodBodyMode.RawAsm,
        MethodBodyMode.Isil,
        MethodBodyMode.Pseudocode,
    };
}