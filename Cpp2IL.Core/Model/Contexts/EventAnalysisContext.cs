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

    public virtual TypeAnalysisContext DefaultEventType => AppContext.ResolveIl2CppType(Definition?.RawType) ?? throw new($"Subclasses must override {nameof(DefaultEventType)}.");

    public TypeAnalysisContext? OverrideEventType { get; set; }

    public TypeAnalysisContext EventType
    {
        get => OverrideEventType ?? DefaultEventType;
        set => OverrideEventType = value;
    }

    public MethodAttributes Visibility
    {
        get
        {
            // Determine the most permissive visibility among the adder, remover, and invoker
            var adderVisibility = Adder?.Visibility ?? MethodAttributes.PrivateScope;
            var removerVisibility = Remover?.Visibility ?? MethodAttributes.PrivateScope;
            var invokerVisibility = Invoker?.Visibility ?? MethodAttributes.PrivateScope;

            // public
            if (adderVisibility == MethodAttributes.Public || removerVisibility == MethodAttributes.Public || invokerVisibility == MethodAttributes.Public)
                return MethodAttributes.Public;

            // protected internal
            if (adderVisibility == MethodAttributes.FamORAssem || removerVisibility == MethodAttributes.FamORAssem || invokerVisibility == MethodAttributes.FamORAssem)
                return MethodAttributes.FamORAssem;

            // internal
            if (adderVisibility == MethodAttributes.Assembly || removerVisibility == MethodAttributes.Assembly || invokerVisibility == MethodAttributes.Assembly)
                return MethodAttributes.Assembly;

            // protected
            if (adderVisibility == MethodAttributes.Family || removerVisibility == MethodAttributes.Family || invokerVisibility == MethodAttributes.Family)
                return MethodAttributes.Family;

            // private protected
            if (adderVisibility == MethodAttributes.FamANDAssem || removerVisibility == MethodAttributes.FamANDAssem || invokerVisibility == MethodAttributes.FamANDAssem)
                return MethodAttributes.FamANDAssem;

            // private
            return MethodAttributes.Private;
        }
    }

    public virtual bool IsStatic => Definition?.IsStatic ?? throw new($"Subclasses must override {nameof(IsStatic)}.");

    public bool IsAbstract => Adder?.IsAbstract ?? Remover?.IsAbstract ?? Invoker?.IsAbstract ?? false;

    public bool IsVirtual => Adder?.IsVirtual ?? Remover?.IsVirtual ?? Invoker?.IsVirtual ?? false;

    public bool IsNewSlot => Adder?.IsNewSlot ?? Remover?.IsNewSlot ?? Invoker?.IsNewSlot ?? false;

    public bool IsFinal => Adder?.IsFinal ?? Remover?.IsFinal ?? Invoker?.IsFinal ?? false;

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
