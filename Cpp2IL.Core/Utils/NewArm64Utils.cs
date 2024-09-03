using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Logging;
using Disarm;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class NewArm64Utils
{
    public static List<Arm64Instruction> GetArm64MethodBodyAtVirtualAddress(ulong virtAddress, bool managed = true, int count = -1)
    {
        if (managed)
        {
            var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtAddress);

            //We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
            if (startOfNext > 0)
            {
                var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(startOfNext);

                var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtAddress);
                if (rawStartOfNextMethod < rawStart)
                    rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;

                var bytes = LibCpp2IlMain.Binary.GetRawBinaryContent().AsSpan((int)rawStart, (int)(rawStartOfNextMethod - rawStart));

                return Disassemble(bytes, virtAddress);
            }
        }

        //Unmanaged function, look for first b
        var pos = (int)LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(virtAddress);
        var allBytes = LibCpp2IlMain.Binary.GetRawBinaryContent();
        var span = allBytes.AsSpan(pos, 4);
        List<Arm64Instruction> ret = new();
        
        while ((count == -1 || ret.Count < count) &&
               !ret.Any( Predicate)) // 条件分支指令)
        {
            ret = Disassemble(span, virtAddress);
            // Logger.InfoNewline("parser ins "+ret[^1] +" MnemonicConditionCode"+ret[^1].MnemonicConditionCode);
            //All arm64 instructions are 4 bytes
            span = allBytes.AsSpan(pos, span.Length + 4);
        }
        return ret;
    }

    private static bool Predicate(Arm64Instruction arg)
    {
        if (arg.Mnemonic==Arm64Mnemonic.B && arg.MnemonicConditionCode!=Arm64ConditionCode.NONE)
        {
            return false;
        }

        if (arg.Mnemonic==Arm64Mnemonic.BL)
        {
            return true;
        }
        // if (arg.Mnemonic == Arm64Mnemonic.RET)
        // {
        //     return true;
        // }
        // if (arg.Mnemonic==Arm64Mnemonic.B)
        // {
        //     return true;
        // }
        //
        return false;
    }

    private static List<Arm64Instruction> Disassemble(Span<byte> bytes, ulong virtAddress)
    {
        try
        {
            return Disassembler.Disassemble(bytes, virtAddress, new Disassembler.Options(true, true, false)).ToList();
        }
        catch (Exception e)
        {
            throw new($"Failed to disassemble method body: {string.Join(", ", bytes.ToArray().Select(b => "0x" + b.ToString("X2")))}", e);
        }
    }

    public static List<Arm64Instruction> ToList(this Disassembler.SpanEnumerator enumerator)
    {
        var ret = new List<Arm64Instruction>();
        while (enumerator.MoveNext())
        {
            ret.Add(enumerator.Current);
        }

        return ret;
    }
    
    public static Arm64Instruction LastValid(this List<Arm64Instruction> list)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Mnemonic is not (Arm64Mnemonic.INVALID or Arm64Mnemonic.UNIMPLEMENTED))
                return list[i];
        }

        return list[^1];
    }
}
