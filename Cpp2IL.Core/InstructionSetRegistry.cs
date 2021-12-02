using System.Collections.Generic;
using Cpp2IL.Core.Model;
using LibCpp2IL;

namespace Cpp2IL.Core;

public static class InstructionSetRegistry
{
    private static Dictionary<InstructionSetId, BaseInstructionSet> _registeredSets = new();

    public static void RegisterInstructionSet<T>(InstructionSetId forId) where T : BaseInstructionSet, new() => _registeredSets.Add(forId, new T());
    
    public static BaseInstructionSet GetInstructionSet(InstructionSetId forId) => _registeredSets[forId];

    static InstructionSetRegistry()
    {
        RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
        RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
    }
}