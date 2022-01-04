using System;
using Cpp2IL.Core.Api;

namespace Cpp2IL.Core.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class RegisterCpp2IlPluginAttribute : Attribute
{
    public Type PluginType { get; }

    public RegisterCpp2IlPluginAttribute(Type pluginType)
    {
        if(!typeof(Cpp2IlPlugin).IsAssignableFrom(pluginType))
            throw new ArgumentException("Plugin type to register must extend Cpp2IlPlugin", nameof(pluginType));
        
        if(pluginType.GetConstructor(Type.EmptyTypes) is null)
            throw new ArgumentException("Plugin type to register must have a public, zero-argument constructor.", nameof(pluginType));

        PluginType = pluginType;
    }
}