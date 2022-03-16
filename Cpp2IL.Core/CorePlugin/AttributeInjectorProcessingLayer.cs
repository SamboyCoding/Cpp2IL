using System;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Model.CustomAttributes;

namespace Cpp2IL.Core.CorePlugin;

public class AttributeInjectorProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Attribute Injector";
    public override string Id => "attributeinjector";

    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        InjectAttributeAttribute(appContext);
    }

    private static void InjectAttributeAttribute(ApplicationAnalysisContext appContext)
    {
        var attributeAttributes = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "AttributeAttribute", appContext.SystemTypes.SystemAttributeType);

        var attributeNameFields = attributeAttributes.InjectFieldToAllAssemblies("Name", appContext.SystemTypes.SystemStringType, FieldAttributes.Public);
        var attributeRvaFields = attributeAttributes.InjectFieldToAllAssemblies("RVA", appContext.SystemTypes.SystemStringType, FieldAttributes.Public);
        var attributeOffsetFields = attributeAttributes.InjectFieldToAllAssemblies("Offset", appContext.SystemTypes.SystemStringType, FieldAttributes.Public);

        var attributeConstructors = attributeAttributes.InjectConstructor(false);

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var nameField = attributeNameFields[assemblyAnalysisContext];
            var rvaField = attributeRvaFields[assemblyAnalysisContext];
            var offsetField = attributeOffsetFields[assemblyAnalysisContext];

            var attributeConstructor = attributeConstructors[assemblyAnalysisContext];

            ProcessCustomAttributesForContext(assemblyAnalysisContext, nameField, rvaField, offsetField, attributeConstructor);

            foreach (var typeAnalysisContext in assemblyAnalysisContext.Types)
            {
                ProcessCustomAttributesForContext(typeAnalysisContext, nameField, rvaField, offsetField, attributeConstructor);

                typeAnalysisContext.Methods.ForEach(m => ProcessCustomAttributesForContext(m, nameField, rvaField, offsetField, attributeConstructor));

                typeAnalysisContext.Fields.ForEach(f => ProcessCustomAttributesForContext(f, nameField, rvaField, offsetField, attributeConstructor));

                typeAnalysisContext.Properties.ForEach(p => ProcessCustomAttributesForContext(p, nameField, rvaField, offsetField, attributeConstructor));

                typeAnalysisContext.Events.ForEach(e => ProcessCustomAttributesForContext(e, nameField, rvaField, offsetField, attributeConstructor));
            }
        }
    }

    private static void ProcessCustomAttributesForContext(HasCustomAttributes context, FieldAnalysisContext nameField, FieldAnalysisContext rvaField, FieldAnalysisContext offsetField, MethodAnalysisContext ctor)
    {
        context.AnalyzeCustomAttributeData();
        
        if(context.CustomAttributes == null)
            return;

        for (var index = 0; index < context.CustomAttributes.Count; index++)
        {
            var attribute = context.CustomAttributes[index];
            
            if (attribute.IsSuitableForEmission)
                //Attribute has all required parameters, so we don't need an injected one
                continue;

            //Create replacement attribute
            var replacementAttribute = new AnalyzedCustomAttribute(ctor);

            //Get ptr, and from it, rva and offset
            var generatorPtr = context.CaCacheGeneratorAnalysis!.UnderlyingPointer;
            var generatorRva = context.AppContext.Binary.GetRVA(generatorPtr);
            if (!context.AppContext.Binary.TryMapVirtualAddressToRaw(generatorPtr, out var offsetInBinary))
                offsetInBinary = 0;
            
            //Add the 3 fields to the replacement attribute
            replacementAttribute.Fields.Add(new(nameField, new CustomAttributePrimitiveParameter(attribute.Constructor.DeclaringType!.Name)));
            replacementAttribute.Fields.Add(new(rvaField, new CustomAttributePrimitiveParameter($"0x{generatorRva:X}")));
            replacementAttribute.Fields.Add(new(offsetField, new CustomAttributePrimitiveParameter($"0x{offsetInBinary:X}")));
            
            //Replace the original attribute with the replacement attribute
            context.CustomAttributes[index] = replacementAttribute;
        }
    }
}