using System;
using System.Reflection;
using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

public sealed class NativeMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer { get; }

    public override string DefaultName { get; }

    public override bool IsStatic => true;

    public override bool IsVoid { get; }

    public override MethodAttributes Attributes => MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig;

    protected override int CustomAttributeIndex => -1;

    public NativeMethodAnalysisContext(TypeAnalysisContext parent, ulong address, bool voidReturn) : base(null, parent)
    {
        if (address == 0)
            throw new ArgumentOutOfRangeException(nameof(address));

        IsVoid = voidReturn;
        UnderlyingPointer = address;
        if (LibCpp2IlMain.Binary?.TryGetExportedFunctionName(UnderlyingPointer, out var name) ?? false)
        {
            DefaultName = name;
        }
        else
        {
            DefaultName = $"NativeMethod_0x{UnderlyingPointer:X}";
        }

        RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
    }
}
