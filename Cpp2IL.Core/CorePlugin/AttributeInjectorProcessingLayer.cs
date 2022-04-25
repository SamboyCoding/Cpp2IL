using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Model.CustomAttributes;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.CorePlugin;

public class AttributeInjectorProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Attribute Injector";
    public override string Id => "attributeinjector";

    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        InjectAttributeAttribute(appContext);
        InjectTokenAttribute(appContext);
        InjectAddressAttribute(appContext);
        InjectFieldOffsetAttribute(appContext);
    }

    private static void InjectFieldOffsetAttribute(ApplicationAnalysisContext appContext)
    {
        var fieldOffsetAttributes = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "FieldOffsetAttribute", appContext.SystemTypes.SystemAttributeType);

        var offsetFields = fieldOffsetAttributes.InjectFieldToAllAssemblies("Offset", appContext.SystemTypes.SystemStringType, FieldAttributes.Public);

        var fieldOffsetConstructors = fieldOffsetAttributes.InjectConstructor(false);

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var offsetField = offsetFields[assemblyAnalysisContext];

            var fieldOffsetConstructor = fieldOffsetConstructors[assemblyAnalysisContext];

            foreach (var f in assemblyAnalysisContext.Types.SelectMany(t => t.Fields))
            {
                if (f.DeclaringType.Name == "FIFJGNHHBKJ")
                    Debugger.Break();

                if (f.CustomAttributes == null || f.BackingData == null || f.IsStatic)
                    continue;

                var newAttribute = new AnalyzedCustomAttribute(fieldOffsetConstructor);

                //This loop is not done parallel because f.Offset has heavy lock contention
                newAttribute.Fields.Add(new(offsetField, new CustomAttributePrimitiveParameter($"0x{f.Offset:X}")));
                f.CustomAttributes.Add(newAttribute);
            }
        }
    }

    private static void InjectAddressAttribute(ApplicationAnalysisContext appContext)
    {
        var addressAttributes = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "AddressAttribute", appContext.SystemTypes.SystemAttributeType);

        var rvaFields = addressAttributes.InjectFieldToAllAssemblies("RVA", appContext.SystemTypes.SystemStringType, FieldAttributes.Public);
        var offsetFields = addressAttributes.InjectFieldToAllAssemblies("Offset", appContext.SystemTypes.SystemStringType, FieldAttributes.Public);

        var addressConstructors = addressAttributes.InjectConstructor(false);

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var rvaField = rvaFields[assemblyAnalysisContext];
            var offsetField = offsetFields[assemblyAnalysisContext];

            var addressConstructor = addressConstructors[assemblyAnalysisContext];

            foreach (var m in assemblyAnalysisContext.Types.SelectMany(t => t.Methods))
            {
                if (m.CustomAttributes == null || m.Definition == null)
                    continue;

                var newAttribute = new AnalyzedCustomAttribute(addressConstructor);
                newAttribute.Fields.Add(new(rvaField, new CustomAttributePrimitiveParameter($"0x{m.Definition.Rva:X}")));
                if (appContext.Binary.TryMapVirtualAddressToRaw(m.UnderlyingPointer, out var offset))
                    newAttribute.Fields.Add(new(offsetField, new CustomAttributePrimitiveParameter($"0x{offset:X}")));
                m.CustomAttributes.Add(newAttribute);
            }
        }
    }

    private static void InjectTokenAttribute(ApplicationAnalysisContext appContext)
    {
        var tokenAttributes = appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "TokenAttribute", appContext.SystemTypes.SystemAttributeType);

        var tokenFields = tokenAttributes.InjectFieldToAllAssemblies("Token", appContext.SystemTypes.SystemStringType, FieldAttributes.Public);

        var tokenConstructors = tokenAttributes.InjectConstructor(false);

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var tokenField = tokenFields[assemblyAnalysisContext];

            var tokenConstructor = tokenConstructors[assemblyAnalysisContext];

            var toProcess = assemblyAnalysisContext.Types.SelectMany(ctx => ctx.Methods.Cast<HasCustomAttributes>()
                    .Concat(ctx.Fields)
                    .Concat(ctx.Events)
                    .Concat(ctx.Properties)
                    .Append(ctx))
                .Append(assemblyAnalysisContext);

            Parallel.ForEach(toProcess, context =>
            {
                if (context.CustomAttributes == null || context.Token == 0)
                    return;

                var newAttribute = new AnalyzedCustomAttribute(tokenConstructor);
                newAttribute.Fields.Add(new(tokenField, new CustomAttributePrimitiveParameter($"0x{context.Token:X}")));
                context.CustomAttributes.Add(newAttribute);
            });
        }
    }

    private static void InjectAttributeAttribute(ApplicationAnalysisContext appContext)
    {
        if (LibCpp2IlMain.MetadataVersion >= 29f)
        {
            //All attributes should be fully serializable anyway, as they're stored in metadata
            //However, we still need to read them all
            var toAnalyze = appContext.Assemblies.SelectMany(ctx => ctx.Types)
                .SelectMany(ctx =>
                    ctx.Methods.SelectMany(m => m.Parameters.Cast<HasCustomAttributes>().Append(m)
                        .Concat(ctx.Fields)
                        .Concat(ctx.Events)
                        .Concat(ctx.Properties)
                        .Append(ctx)));
            
            foreach (var hasCustomAttributes in toAnalyze) 
                hasCustomAttributes.AnalyzeCustomAttributeData();

            // MiscUtils.ExecuteParallel(toAnalyze, ctx => ctx.AnalyzeCustomAttributeData());
            return;
        }

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

            var toProcess = assemblyAnalysisContext.Types
                .SelectMany(ctx => ctx.Methods.SelectMany(m => m.Parameters.Cast<HasCustomAttributes>().Append(m))
                    .Concat(ctx.Fields)
                    .Concat(ctx.Events)
                    .Concat(ctx.Properties)
                    .Append(ctx))
                .Append(assemblyAnalysisContext);

            MiscUtils.ExecuteParallel(toProcess, c => ProcessCustomAttributesForContext(c, nameField, rvaField, offsetField, attributeConstructor));
        }
    }

    private static void ProcessCustomAttributesForContext(HasCustomAttributes context, FieldAnalysisContext nameField, FieldAnalysisContext rvaField, FieldAnalysisContext offsetField, MethodAnalysisContext ctor)
    {
        context.AnalyzeCustomAttributeData();

        if (context.CustomAttributes == null)
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
            var generatorRva = context.AppContext.Binary.GetRva(generatorPtr);
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