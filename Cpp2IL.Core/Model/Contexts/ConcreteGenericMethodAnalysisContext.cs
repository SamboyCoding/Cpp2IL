using System.Collections.Generic;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericMethodAnalysisContext : MethodAnalysisContext
{
    public readonly AssemblyAnalysisContext DeclaringAsm;
    public readonly Cpp2IlMethodRef MethodRef;
    public TypeAnalysisContext BaseTypeContext;
    public MethodAnalysisContext BaseMethodContext;

    public override ulong UnderlyingPointer => MethodRef.GenericVariantPtr;

    public override bool IsStatic => BaseMethodContext.IsStatic;
    
    public override bool IsVoid => BaseMethodContext.IsVoid;

    //TODO do we want to update these two to point at resolved generic types rather than the original generic types? 
    public override List<Il2CppParameterReflectionData> Parameters => BaseMethodContext.Parameters;


    public ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context) : base(context)
    {
        MethodRef = methodRef;
        DeclaringAsm = context.GetAssemblyByName(methodRef.DeclaringType.DeclaringAssembly!.Name!) ?? throw new($"Unable to resolve declaring assembly {methodRef.DeclaringType.DeclaringAssembly.Name} for generic method {methodRef}");
        BaseTypeContext = DeclaringAsm!.GetTypeByFullName(methodRef.DeclaringType.FullName!) ?? throw new($"Unable to resolve declaring type {methodRef.DeclaringType.FullName} for generic method {methodRef}");
        BaseMethodContext = BaseTypeContext.GetMethod(methodRef.BaseMethod) ?? throw new($"Unable to resolve base method {methodRef.BaseMethod} for generic method {methodRef}");
    }
}