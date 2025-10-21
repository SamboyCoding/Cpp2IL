using System;
using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

public class EventAnalysisContext : HasCustomAttributesAndName, IEventInfoProvider
{
    public readonly TypeAnalysisContext DeclaringType;
    public readonly Il2CppEventDefinition? Definition;
    public readonly MethodAnalysisContext? Adder;
    public readonly MethodAnalysisContext? Remover;
    public readonly MethodAnalysisContext? Invoker;

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string DefaultName => Definition?.Name ?? throw new($"Subclasses must override {nameof(DefaultName)}.");

    public virtual EventAttributes DefaultAttributes => (EventAttributes?)Definition?.RawType?.Attrs ?? throw new($"Subclasses must override {nameof(DefaultAttributes)}.");

    public EventAttributes? OverrideAttributes { get; set; }

    public EventAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    public virtual TypeAnalysisContext DefaultEventType => DeclaringType.DeclaringAssembly.ResolveIl2CppType(Definition?.RawType) ?? throw new($"Subclasses must override {nameof(DefaultEventType)}.");

    public TypeAnalysisContext? OverrideEventType { get; set; }

    public TypeAnalysisContext EventType
    {
        get => OverrideEventType ?? DefaultEventType;
        set => OverrideEventType = value;
    }

    public virtual bool IsStatic => Definition?.IsStatic ?? throw new($"Subclasses must override {nameof(IsStatic)}.");

    public EventAnalysisContext(Il2CppEventDefinition definition, TypeAnalysisContext parent) : base(definition.token, parent.AppContext)
    {
        Definition = definition;
        DeclaringType = parent;

        InitCustomAttributeData();

        Adder = parent.GetMethod(definition.Adder);
        Remover = parent.GetMethod(definition.Remover);
        Invoker = parent.GetMethod(definition.Invoker);
    }

    protected EventAnalysisContext(MethodAnalysisContext? adder, MethodAnalysisContext? remover, MethodAnalysisContext? invoker, TypeAnalysisContext parent) : base(0, parent.AppContext)
    {
        if (adder is null && remover is null && invoker is null)
            throw new ArgumentException("Event must have at least one method");

        DeclaringType = parent;
        Adder = adder;
        Remover = remover;
        Invoker = invoker;
    }

    public override string ToString() => $"Event: {DeclaringType.Name}::{Name}";

    #region StableNameDotNet Impl

    public ITypeInfoProvider EventTypeInfoProvider => Definition!.RawType!.ThisOrElementIsGenericParam()
        ? new GenericParameterTypeInfoProviderWrapper(Definition.RawType!.GetGenericParamName())
        : TypeAnalysisContext.GetSndnProviderForType(AppContext, Definition.RawType!);

    public string EventName => Name;

    #endregion
}
