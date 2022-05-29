using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericMethodAnalysisContext : MethodAnalysisContext
{
    public readonly AssemblyAnalysisContext DeclaringAsm;
    public readonly Cpp2IlMethodRef MethodRef;
    public TypeAnalysisContext BaseTypeContext;
    public MethodAnalysisContext BaseMethodContext;

    public sealed override ulong UnderlyingPointer => MethodRef.GenericVariantPtr;

    public override bool IsStatic => BaseMethodContext.IsStatic;
    
    public override bool IsVoid => BaseMethodContext.IsVoid;


    public ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context) : base(context)
    {
        MethodRef = methodRef;
        DeclaringAsm = context.GetAssemblyByName(methodRef.DeclaringType.DeclaringAssembly!.Name!) ?? throw new($"Unable to resolve declaring assembly {methodRef.DeclaringType.DeclaringAssembly.Name} for generic method {methodRef}");
        BaseTypeContext = DeclaringAsm!.GetTypeByFullName(methodRef.DeclaringType.FullName!) ?? throw new($"Unable to resolve declaring type {methodRef.DeclaringType.FullName} for generic method {methodRef}");
        BaseMethodContext = BaseTypeContext.GetMethod(methodRef.BaseMethod) ?? throw new($"Unable to resolve base method {methodRef.BaseMethod} for generic method {methodRef}");

        //TODO: Do we want to update this to populate known generic parameters based on the generic arguments? 
        Parameters.AddRange(BaseMethodContext.Parameters);
        
        if(UnderlyingPointer != 0)
            RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
    }
}