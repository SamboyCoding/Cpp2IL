using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model;

public class MultiAssemblyInjectedType(InjectedTypeAnalysisContext[] injectedTypes)
{
    public InjectedTypeAnalysisContext[] InjectedTypes { get; } = injectedTypes;

    public Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext> InjectMethodToAllAssemblies(string name, TypeAnalysisContext returnType, MethodAttributes attributes, params ReadOnlySpan<TypeAnalysisContext> args)
    {
        var dictionary = new Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext>();
        foreach (var type in InjectedTypes)
        {
            dictionary[type.DeclaringAssembly] = type.InjectMethodContext(name, returnType, attributes, args);
        }
        return dictionary;
    }

    public Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext> InjectConstructor(bool isStatic, params ReadOnlySpan<TypeAnalysisContext> args)
    {
        var attributes = isStatic
            ? MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Static
            : MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig;
        return InjectMethodToAllAssemblies(isStatic ? ".cctor" : ".ctor", InjectedTypes.First().AppContext.SystemTypes.SystemVoidType, attributes, args);
    }

    public Dictionary<AssemblyAnalysisContext, InjectedFieldAnalysisContext> InjectFieldToAllAssemblies(string name, TypeAnalysisContext fieldType, FieldAttributes attributes)
        => InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t => t.InjectFieldContext(name, fieldType, attributes));

    public Dictionary<AssemblyAnalysisContext, InjectedPropertyAnalysisContext> InjectPropertyToAllAssemblies(string name, TypeAnalysisContext propertyType, Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext>? getter, Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext>? setter, PropertyAttributes attributes)
        => InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t => t.InjectPropertyContext(name, propertyType, getter?[t.DeclaringAssembly], setter?[t.DeclaringAssembly], attributes));

    public Dictionary<AssemblyAnalysisContext, InjectedEventAnalysisContext> InjectEventToAllAssemblies(string name, TypeAnalysisContext eventType, Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext>? adder, Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext>? remover, Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext>? invoker, EventAttributes attributes)
        => InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t => t.InjectEventContext(name, eventType, adder?[t.DeclaringAssembly], remover?[t.DeclaringAssembly], invoker?[t.DeclaringAssembly], attributes));

    public MultiAssemblyInjectedType InjectNestedType(string name, TypeAnalysisContext? baseType, TypeAttributes attributes = TypeAttributes.NestedPublic | TypeAttributes.Sealed)
        => new(InjectedTypes.Select(t => t.InjectNestedType(name, baseType, attributes)).ToArray());
}
