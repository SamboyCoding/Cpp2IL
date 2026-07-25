using System;
using System.Collections.Generic;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Finds exception throw helper functions. These eventually look up the type they throw by name
/// (via <c>Class::FromName(corlib, "System", "NullReferenceException")</c>), so the type name appears
/// as a plain C string in the helper's body.
/// </summary>
public static class ThrowHelperRecovery
{
    private const int MaxDepth = 5;
    private const int MaxStringLength = 64;

    public static TypeAnalysisContext? GetThrownException(ApplicationAnalysisContext appContext, ulong address)
    {
        var name = ResolveName(appContext, address, 0);

        if (name == null)
            return null;

        var type = appContext.LibCpp2IlContext.ReflectionCache.GetType(name);

        return type == null ? null : appContext.ResolveContextForType(type);
    }

    private static string? ResolveName(ApplicationAnalysisContext appContext, ulong address, int depth)
    {
        if (appContext.ThrowHelperNamesByAddress.TryGetValue(address, out var cached))
            return cached;

        if (address == 0 || depth >= MaxDepth)
            return null;

        // Insert before recursing so a cycle terminates
        appContext.ThrowHelperNamesByAddress[address] = null;

        InstructionList body;

        try
        {
            body = X86Utils.GetMethodBodyAtVirtAddressNew(address, true, appContext.Binary);
        }
        catch
        {
            return null;
        }

        var name = FindExceptionName(appContext, body);

        if (name == null)
        {
            foreach (var instruction in body)
            {
                if (instruction.Mnemonic != Mnemonic.Call || instruction.Op0Kind != OpKind.NearBranch64)
                    continue;

                name = ResolveName(appContext, instruction.NearBranchTarget, depth + 1);

                if (name != null)
                    break;
            }
        }

        appContext.ThrowHelperNamesByAddress[address] = name;
        return name;
    }

    private static string? FindExceptionName(ApplicationAnalysisContext appContext, InstructionList body)
    {
        foreach (var instruction in body)
        {
            if (instruction.Mnemonic != Mnemonic.Lea || !instruction.IsIPRelativeMemoryOperand)
                continue;

            if (ReadCStringAtVirtualAddress(appContext, instruction.IPRelativeMemoryAddress) is { } text && text.EndsWith("Exception", StringComparison.Ordinal))
                return text;
        }

        return null;
    }

    //TODO didn't we have a helper for this somewhere? Can't find it. Maybe got deleted. Maybe it's just too late
    private static string? ReadCStringAtVirtualAddress(ApplicationAnalysisContext appContext, ulong address)
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

        while (end < content.Length && end - offset < MaxStringLength && content[(int)end] != 0)
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
