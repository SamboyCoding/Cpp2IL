using System.Collections.Generic;
using System.Reflection;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer => 0;

    public override string DefaultName { get; }

    public override bool IsStatic => Attributes.HasFlag(MethodAttributes.Static);

    public override bool IsVoid => InjectedReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_VOID;

    public override MethodAttributes Attributes { get; }
    
    protected override bool IsInjected => true;

    protected override int CustomAttributeIndex => -1;

    public override IEnumerable<MethodAnalysisContext> Overrides => OverridesList;
    public List<MethodAnalysisContext> OverridesList { get; } = [];

    public InjectedMethodAnalysisContext(TypeAnalysisContext parent, string name, TypeAnalysisContext returnType, MethodAttributes attributes, TypeAnalysisContext[] injectedParameterTypes, string[]? injectedParameterNames = null) : base(null, parent)
    {
        DefaultName = name;
        InjectedReturnType = returnType;
        Attributes = attributes;

        for (var i = 0; i < injectedParameterTypes.Length; i++)
        {
            var injectedParameterType = injectedParameterTypes[i];
            var injectedParameterName = injectedParameterNames?[i];

            Parameters.Add(new InjectedParameterAnalysisContext(injectedParameterName, injectedParameterType, i, this));
        }
    }
}
