using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedEventAnalysisContext : EventAnalysisContext
{
    public override string DefaultName { get; }
    public override EventAttributes DefaultAttributes { get; }
    public override TypeAnalysisContext DefaultEventType { get; }
    public override bool IsStatic
    {
        get
        {
            if (Adder is not null)
                return Adder.IsStatic;
            if (Remover is not null)
                return Remover.IsStatic;
            if (Invoker is not null)
                return Invoker.IsStatic;

            throw new("Event has no methods");
        }
    }
    protected override bool IsInjected => true;

    public InjectedEventAnalysisContext(
        string name,
        TypeAnalysisContext eventType,
        MethodAnalysisContext? adder,
        MethodAnalysisContext? remover,
        MethodAnalysisContext? invoker,
        EventAttributes eventAttributes,
        TypeAnalysisContext parent) : base(adder, remover, invoker, parent)
    {
        DefaultName = name;
        DefaultEventType = eventType;
        DefaultAttributes = eventAttributes;
    }
}
