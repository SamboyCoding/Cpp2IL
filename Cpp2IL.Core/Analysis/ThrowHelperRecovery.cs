using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Finds exception throw helper functions. These eventually look up the type they throw by name
/// (via <c>Class::FromName(corlib, "System", "NullReferenceException")</c>), so the type name appears
/// as a plain C string in the helper's body.
/// </summary>
public static class ThrowHelperRecovery
{
    private const int MaxDepth = 5;
    private const int MaxRaiserDepth = 3;
    private const int MaxStringLength = 64;

    public static TypeAnalysisContext? GetThrownException(ApplicationAnalysisContext appContext, ulong address)
    {
        var name = ResolveName(appContext, address, 0);

        if (name == null)
            return null;

        var type = appContext.LibCpp2IlContext.ReflectionCache.GetType(name);

        return type == null ? null : appContext.ResolveContextForType(type);
    }

    // Whether the provided method raises whatever exception it is handed
    // e.g. il2cpp_codegen_raise_exception, il2cpp_codegen_rethrow_exception, vm::Exception::Raise
    public static bool IsExceptionRaiser(ApplicationAnalysisContext appContext, ulong address)
    {
        if (address == 0)
            return false;

        if (appContext.ExceptionRaisersByAddress.TryGetValue(address, out var cached))
            return cached;

        var raise = appContext.GetOrCreateKeyFunctionAddresses().il2cpp_vm_exception_raise;

        if (raise == 0)
            return false;

        // grab the c++ exception raise method from the end of il2cpp::vm::Exception::Raise
        var nativeThrow = appContext.InstructionSet.InspectPotentialThrowHelper(appContext, raise).CallTargets.LastOrDefault();

        // and then check if we're calling it
        var result = nativeThrow != 0 && ReachesCall(appContext, address, nativeThrow, 0, []);

        appContext.ExceptionRaisersByAddress[address] = result;
        return result;
    }

    private static bool ReachesCall(ApplicationAnalysisContext appContext, ulong address, ulong wanted, int depth, HashSet<ulong> visited)
    {
        if (address == wanted)
            return true;

        if (depth >= MaxRaiserDepth || !visited.Add(address))
            return false;

        var (_, callTargets) = appContext.InstructionSet.InspectPotentialThrowHelper(appContext, address);

        return callTargets.Any(target => ReachesCall(appContext, target, wanted, depth + 1, visited));
    }

    private static string? ResolveName(ApplicationAnalysisContext appContext, ulong address, int depth)
    {
        if (appContext.ThrowHelperNamesByAddress.TryGetValue(address, out var cached))
            return cached;

        if (address == 0 || depth >= MaxDepth)
            return null;

        // Insert before recursing so a cycle terminates
        appContext.ThrowHelperNamesByAddress[address] = null;

        var (dataReferences, callTargets) = appContext.InstructionSet.InspectPotentialThrowHelper(appContext, address);

        var name = FindExceptionName(appContext, dataReferences);

        if (name == null)
        {
            foreach (var target in callTargets)
            {
                name = ResolveName(appContext, target, depth + 1);

                if (name != null)
                    break;
            }
        }

        appContext.ThrowHelperNamesByAddress[address] = name;
        return name;
    }

    private static string? FindExceptionName(ApplicationAnalysisContext appContext, IReadOnlyList<ulong> dataReferences)
    {
        foreach (var address in dataReferences)
            if (ReadCStringAtVirtualAddress(appContext, address) is { } text && text.EndsWith("Exception", StringComparison.Ordinal))
                return text;

        return null;
    }

    //TODO didn't we have a helper for this somewhere? Can't find it. Maybe got deleted. Maybe it's just too late
    internal static string? ReadCStringAtVirtualAddress(ApplicationAnalysisContext appContext, ulong address, int maxLength = MaxStringLength)
    {
        long offset;

        try
        {
            offset = appContext.Binary.MapVirtualAddressToRaw(address, false);
        }
        catch
        {
            return null;
        }

        if (offset <= 0)
            return null;

        var content = appContext.Binary.GetRawBinaryContent();
        var end = offset;

        while (end < content.Length && end - offset < maxLength && content[(int)end] != 0)
        {
            var c = content[(int)end];

            if (c < 32 || c >= 127)
                return null;

            end++;
        }

        if (end == offset || end >= content.Length || content[(int)end] != 0)
            return null;

        var characters = new char[end - offset];

        for (var i = 0; i < characters.Length; i++)
            characters[i] = (char)content[(int)offset + i];

        return new string(characters);
    }
}
