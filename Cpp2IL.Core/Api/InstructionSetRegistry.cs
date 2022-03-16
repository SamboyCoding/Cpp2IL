using System.Collections.Generic;
using LibCpp2IL;

namespace Cpp2IL.Core.Api;

public static class InstructionSetRegistry
{
    private static Dictionary<InstructionSetId, Cpp2IlInstructionSet> _registeredSets = new();

    public static void RegisterInstructionSet<T>(InstructionSetId forId) where T : Cpp2IlInstructionSet, new() => _registeredSets.Add(forId, new T());
    
    public static Cpp2IlInstructionSet GetInstructionSet(InstructionSetId forId) => _registeredSets[forId];
}