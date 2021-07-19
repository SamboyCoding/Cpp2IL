using Mono.Cecil;
using System;

namespace Cpp2IL.Core
{
	internal static class AssemblyUnpopulator
    {
        internal const string InjectedNamespaceName = "Cpp2IlInjected";

        public static void UnpopulateStubTypesInAssembly(AssemblyDefinition imageDef)
        {
            TypeDefinition addressAttribute = imageDef.MainModule.GetType(InjectedNamespaceName + ".AddressAttribute");
            TypeDefinition fieldOffsetAttribute = imageDef.MainModule.GetType(InjectedNamespaceName + ".FieldOffsetAttribute");
            TypeDefinition attributeAttribute = imageDef.MainModule.GetType(InjectedNamespaceName + ".AttributeAttribute");
            TypeDefinition metadataOffsetAttribute = imageDef.MainModule.GetType(InjectedNamespaceName + ".MetadataOffsetAttribute");
            TypeDefinition tokenAttribute = imageDef.MainModule.GetType(InjectedNamespaceName + ".TokenAttribute");
            var attributeTypes = new TypeDefinition[] { addressAttribute, fieldOffsetAttribute, attributeAttribute, metadataOffsetAttribute, tokenAttribute };

            foreach (var type in imageDef.MainModule.Types!)
                RemoveInjectedAttributesFromType(type, attributeTypes);

            imageDef.MainModule.Types.Remove(addressAttribute);
            imageDef.MainModule.Types.Remove(fieldOffsetAttribute);
            imageDef.MainModule.Types.Remove(attributeAttribute);
            imageDef.MainModule.Types.Remove(metadataOffsetAttribute);
            imageDef.MainModule.Types.Remove(tokenAttribute);
        }

        private static void RemoveInjectedAttributesFromType(TypeDefinition type, TypeDefinition[] attributeTypes)
        {
            try
            {
                foreach (var field in type.Fields)
                    RemoveInjectedAttribute(field.CustomAttributes, attributeTypes);
                foreach (var @event in type.Events)
                    RemoveInjectedAttribute(@event.CustomAttributes, attributeTypes);
                foreach (var property in type.Properties)
                    RemoveInjectedAttribute(property.CustomAttributes, attributeTypes);
                foreach (var method in type.Methods)
                    RemoveInjectedAttribute(method.CustomAttributes, attributeTypes);
                RemoveInjectedAttribute(type.CustomAttributes, attributeTypes);

                foreach (var nestedType in type.NestedTypes)
                    RemoveInjectedAttributesFromType(nestedType, attributeTypes);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to process type {type.FullName} (module {type.Module.Name}, declaring type {type.DeclaringType?.FullName})", e);
            }
        }

        private static void RemoveInjectedAttribute(Mono.Collections.Generic.Collection<CustomAttribute> customAttributes, TypeDefinition[] attributeTypes)
        {
            foreach (var attributeType in attributeTypes)
                RemoveInjectedAttribute(customAttributes, attributeType);
        }

        private static void RemoveInjectedAttribute(Mono.Collections.Generic.Collection<CustomAttribute> customAttributes, TypeDefinition attributeType)
        {
            foreach (var attr in customAttributes.ToArray())
            {
                if (attr.AttributeType == attributeType) customAttributes.Remove(attr);
            }
        }
    }
}
