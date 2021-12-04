using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericMethodAnalysisContext : MethodAnalysisContext
{
    public readonly AssemblyAnalysisContext? DeclaringAsm;
    public readonly Cpp2IlMethodRef MethodRef;

    public override ulong UnderlyingPointer => MethodRef.GenericVariantPtr;


    public ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context) : base(context)
    {
        MethodRef = methodRef;
        DeclaringAsm = context.GetAssemblyByName(methodRef.DeclaringType.DeclaringAssembly!.Name!);
    }
}