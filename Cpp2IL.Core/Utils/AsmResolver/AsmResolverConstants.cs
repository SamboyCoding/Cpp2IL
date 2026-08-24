using System;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Cpp2IL.Core.Utils.AsmResolver;

public static class AsmResolverConstants
{
    private static readonly Constant Null = Constant.FromNull();

    private static readonly Constant[] IntegerCache = new Constant[16];
    private static readonly Constant[] ByteCache = new Constant[16];

    private static readonly Constant SingleZero = Constant.FromValue(0.0F);

    private static readonly Constant BoolFalse = Constant.FromValue(false);
    private static readonly Constant BoolTrue = Constant.FromValue(true);

    static AsmResolverConstants()
    {
        for (var i = 0; i < 16; i++)
        {
            IntegerCache[i] = Constant.FromValue(i);
            ByteCache[(byte)i] = Constant.FromValue((byte)i);
        }
    }

    public static Constant GetOrCreateConstant(object? from)
    {
        return from switch
        {
            null => Null,
            string s => new(ElementType.String, new(Encoding.Unicode.GetBytes(s))),
            bool b => b ? BoolTrue : BoolFalse,
            byte and >= 0 and < 16 => ByteCache[(byte)@from],
            float and 0 => SingleZero,
            >= 0 and < 16 => IntegerCache[(int)from],
            _ => CreateNewConstant((IConvertible)from),
        };
    }

    private static Constant CreateNewConstant(IConvertible from)
    {
        return new(GetElementTypeFromConstant(from), new(MiscUtils.RawBytes(from)));
    }

    private static ElementType GetElementTypeFromConstant(IConvertible primitive)
        => primitive switch
            {
                sbyte => ElementType.I1,
                byte => ElementType.U1,
                bool => ElementType.Boolean,
                short => ElementType.I2,
                ushort => ElementType.U2,
                int => ElementType.I4,
                uint => ElementType.U4,
                long => ElementType.I8,
                ulong => ElementType.U8,
                float => ElementType.R4,
                double => ElementType.R8,
                string => ElementType.String,
                char => ElementType.Char,
                _ => throw new($"Can't get a element type for the constant {primitive} of type {primitive.GetType()}"),
            };
}
