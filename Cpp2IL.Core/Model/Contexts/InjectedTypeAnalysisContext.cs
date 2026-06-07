using System;
using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedTypeAnalysisContext : TypeAnalysisContext
{
    public override string DefaultName { get; }

    public override string DefaultNamespace { get; }

    public override TypeAnalysisContext? DefaultBaseType { get; }

    public override TypeAttributes DefaultAttributes { get; }
    
    protected override bool IsInjected => true;

    public InjectedTypeAnalysisContext(AssemblyAnalysisContext containingAssembly, string ns, string name, TypeAnalysisContext? baseType, TypeAttributes typeAttributes) : base(null, containingAssembly)
    {
        DefaultName = name;
        DefaultNamespace = ns;
        DefaultBaseType = baseType;
        DefaultAttributes = typeAttributes;
    }

    public InjectedMethodAnalysisContext InjectMethodContext(string methodName, TypeAnalysisContext returnType, MethodAttributes attributes, params ReadOnlySpan<TypeAnalysisContext> args)
    {
        var method = new InjectedMethodAnalysisContext(this, methodName, returnType, attributes, args);
        Methods.Add(method);

        return method;
    }

    public InjectedFieldAnalysisContext InjectFieldContext(string fieldName, TypeAnalysisContext fieldType, FieldAttributes attributes)
    {
        var field = new InjectedFieldAnalysisContext(fieldName, fieldType, attributes, this);
        Fields.Add(field);
        return field;
    }

    public InjectedEventAnalysisContext InjectEventContext(
        string eventName,
        TypeAnalysisContext eventType,
        MethodAnalysisContext? adder,
        MethodAnalysisContext? remover,
        MethodAnalysisContext? invoker,
        EventAttributes eventAttributes)
    {
        var @event = new InjectedEventAnalysisContext(eventName, eventType, adder, remover, invoker, eventAttributes, this);
        Events.Add(@event);
        return @event;
    }

    public InjectedPropertyAnalysisContext InjectPropertyContext(
        string propertyName,
        TypeAnalysisContext propertyType,
        MethodAnalysisContext? getter,
        MethodAnalysisContext? setter,
        PropertyAttributes propertyAttributes)
    {
        var property = new InjectedPropertyAnalysisContext(propertyName, propertyType, getter, setter, propertyAttributes, this);
        Properties.Add(property);
        return property;
    }
}
