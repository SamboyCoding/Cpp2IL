using System;
using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public sealed class NativeMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer { get; }

    public override string DefaultName { get; }

    protected override bool IsInjected => true;

    public override TypeAnalysisContext DefaultReturnType => isVoid ? AppContext.SystemTypes.SystemVoidType : AppContext.SystemTypes.SystemObjectType;

    public override MethodAttributes DefaultAttributes => MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig;

    public override MethodImplAttributes DefaultImplAttributes => MethodImplAttributes.Managed;

    protected override int CustomAttributeIndex => -1;

    private readonly bool isVoid;

    public NativeMethodAnalysisContext(TypeAnalysisContext parent, ulong address, bool voidReturn) : base(null, parent)
    {
        if (address == 0)
            throw new ArgumentOutOfRangeException(nameof(address));

        isVoid = voidReturn;
        UnderlyingPointer = address;
        if (AppContext.Binary.TryGetExportedFunctionName(UnderlyingPointer, out var name))
        {
            DefaultName = name;
        }
        else
        {
            DefaultName = $"NativeMethod_0x{UnderlyingPointer:X}";
        }
    }
}
