using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericMethodAnalysisContext : MethodAnalysisContext
{
    public readonly AssemblyAnalysisContext DeclaringAsm;
    public readonly Cpp2IlMethodRef MethodRef;
    public readonly MethodAnalysisContext BaseMethodContext;

    public sealed override ulong UnderlyingPointer => MethodRef.GenericVariantPtr;

    public override bool IsStatic => BaseMethodContext.IsStatic;
    
    public override bool IsVoid => BaseMethodContext.IsVoid;

    public override string DefaultName => BaseMethodContext.DefaultName;

    public override MethodAttributes Attributes => BaseMethodContext.Attributes;

    public override AssemblyAnalysisContext CustomAttributeAssembly => BaseMethodContext.CustomAttributeAssembly;

    public ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context)
        : this(methodRef, ResolveDeclaringAssembly(methodRef, context))
    {
    }

    private ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, AssemblyAnalysisContext declaringAssembly)
        : base(null, ResolveDeclaringType(methodRef, declaringAssembly))
    {
        MethodRef = methodRef;
        DeclaringAsm = declaringAssembly;
        BaseMethodContext = ResolveBaseMethod(methodRef, declaringAssembly.GetTypeByDefinition(methodRef.DeclaringType)!);

        //TODO: Do we want to update this to populate known generic parameters based on the generic arguments? 
        Parameters.AddRange(BaseMethodContext.Parameters);

        if(UnderlyingPointer != 0)
            RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
    }

    private static AssemblyAnalysisContext ResolveDeclaringAssembly(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context)
    {
        return context.GetAssemblyByName(methodRef.DeclaringType.DeclaringAssembly!.Name!)
            ?? throw new($"Unable to resolve declaring assembly {methodRef.DeclaringType.DeclaringAssembly.Name} for generic method {methodRef}");
    }

    private static TypeAnalysisContext ResolveDeclaringType(Cpp2IlMethodRef methodRef, AssemblyAnalysisContext declaringAssembly)
    {
        var baseType = declaringAssembly.AppContext.ResolveContextForType(methodRef.DeclaringType)
            ?? throw new($"Unable to resolve declaring type {methodRef.DeclaringType.FullName} for generic method {methodRef}");

        var genericParams = new TypeAnalysisContext[methodRef.TypeGenericParams.Length];
        for (var i = 0; i < methodRef.TypeGenericParams.Length; i++)
        {
            genericParams[i] = methodRef.TypeGenericParams[i].ToContext(declaringAssembly)
                ?? throw new($"Unable to resolve generic parameter {methodRef.TypeGenericParams[i]} for generic method {methodRef}");
        }
        return new GenericInstanceTypeAnalysisContext(baseType, genericParams, declaringAssembly);
    }

    private static MethodAnalysisContext ResolveBaseMethod(Cpp2IlMethodRef methodRef, TypeAnalysisContext declaringType)
    {
        return declaringType.GetMethod(methodRef.BaseMethod)
            ?? throw new($"Unable to resolve base method {methodRef.BaseMethod} for generic method {methodRef}");
    }
}
