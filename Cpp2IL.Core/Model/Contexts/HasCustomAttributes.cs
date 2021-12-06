using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Model.CustomAttributes;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// A base class to represent any type which has, or can have, custom attributes.
/// </summary>
public abstract class HasCustomAttributes : HasToken
{
    /// <summary>
    /// On V29, stores the custom attribute blob. Pre-29, stores the bytes for the custom attribute generator function.
    /// </summary>
    public byte[] RawIl2CppCustomAttributeData;

    /// <summary>
    /// Stores the analyzed custom attribute data once analysis has actually run.
    /// </summary>
    public List<AnalyzedCustomAttribute>? CustomAttributes;

    /// <summary>
    /// Stores the attribute type range for this member, which references which custom attributes are present.
    ///
    /// Null on v29+, nonnull prior to that
    /// </summary>
    public Il2CppCustomAttributeTypeRange? AttributeTypeRange;

    /// <summary>
    /// Stores the raw types of the custom attributes on this member.
    ///
    /// Null on v29+ (constructors are in the blob), nonnull prior to that
    /// </summary>
    public List<Il2CppType>? AttributeTypes;

    /// <summary>
    /// Prior to v29, stores the analysis context for custom attribute cache generator function.
    ///
    /// On v29, is null because there is no method, the attribute blob is stored instead, in the metadata file.
    /// </summary>
    public AttributeGeneratorMethodAnalysisContext? CaCacheGeneratorAnalysis;

    /// <summary>
    /// Returns this member's custom attribute index, or -1 if it has no custom attributes.
    /// </summary>
    protected abstract int CustomAttributeIndex { get; }

    /// <summary>
    /// Returns this member's assembly context for use in custom attribute reconstruction.
    /// </summary>
    protected abstract AssemblyAnalysisContext CustomAttributeAssembly { get; }

    
#pragma warning disable CS8618 //Non-null member is not initialized.
    protected HasCustomAttributes(uint token, ApplicationAnalysisContext appContext) : base(token, appContext)
    {
        
    }
#pragma warning restore CS8618

    protected void InitCustomAttributeData()
    {
        if (AppContext.MetadataVersion >= 29)
        {
            var target = new Il2CppCustomAttributeDataRange() {token = Token};
            var caIndex = AppContext.Metadata.AttributeDataRanges.BinarySearch
            (
                CustomAttributeAssembly.Definition.Image.customAttributeStart,
                (int) CustomAttributeAssembly.Definition.Image.customAttributeCount,
                target,
                new TokenComparer()
            );

            if (caIndex < 0)
            {
                RawIl2CppCustomAttributeData = Array.Empty<byte>();
                return;
            }

            var attributeDataRange = AppContext.Metadata.AttributeDataRanges[caIndex];
            var next = AppContext.Metadata.AttributeDataRanges[caIndex + 1];

            var blobStart = AppContext.Metadata.metadataHeader.attributeDataOffset + attributeDataRange.startOffset;
            var blobEnd = AppContext.Metadata.metadataHeader.attributeDataOffset + next.startOffset;
            RawIl2CppCustomAttributeData = AppContext.Metadata.ReadByteArrayAtRawAddress(blobStart, (int) (blobEnd - blobStart));
            
            return;
        }

        AttributeTypeRange = AppContext.Metadata.GetCustomAttributeData(CustomAttributeAssembly.Definition.Image, CustomAttributeIndex, Token);

        if (AttributeTypeRange == null)
        {
            RawIl2CppCustomAttributeData = Array.Empty<byte>();
            AttributeTypes = new();
            return; //No attributes
        }

        AttributeTypes = Enumerable.Range(AttributeTypeRange.start, AttributeTypeRange.count)
            .Select(attrIdx => LibCpp2IlMain.TheMetadata!.attributeTypes[attrIdx])
            .Select(typeIdx => LibCpp2IlMain.Binary!.GetType(typeIdx))
            .ToList();

        var rangeIndex = AppContext.Metadata.attributeTypeRanges.IndexOf(AttributeTypeRange);
        ulong generatorPtr;
        if (AppContext.MetadataVersion < 27)
            try
            {
                generatorPtr = AppContext.Binary.GetCustomAttributeGenerator(rangeIndex);
            }
            catch (IndexOutOfRangeException)
            {
                Logger.WarnNewline("Custom attribute generator out of range for " + this, "CA Restore");
                RawIl2CppCustomAttributeData = Array.Empty<byte>();
                return; //Bail out
            }
        else
        {
            var baseAddress = CustomAttributeAssembly.CodeGenModule!.customAttributeCacheGenerator;
            var relativeIndex = rangeIndex - CustomAttributeAssembly.Definition.Image.customAttributeStart;
            var ptrToAddress = baseAddress + (ulong) relativeIndex * (AppContext.Binary.is32Bit ? 4ul : 8ul);
            generatorPtr = AppContext.Binary.ReadClassAtVirtualAddress<ulong>(ptrToAddress);
        }

        if (!AppContext.Binary.TryMapVirtualAddressToRaw(generatorPtr, out _))
        {
            RawIl2CppCustomAttributeData = Array.Empty<byte>();
            return; //Possibly no attributes with params?
        }

        CaCacheGeneratorAnalysis = new(generatorPtr, AppContext);
        RawIl2CppCustomAttributeData = CaCacheGeneratorAnalysis.RawBytes;
    }

    /// <summary>
    /// Attempt to parse the Il2CppCustomAttributeData blob into custom attributes.
    /// </summary>
    public void AnalyzeCustomAttributeData()
    {
        if (AppContext.MetadataVersion >= 29)
            throw new("TODO: V29 blob analysis");

        CaCacheGeneratorAnalysis!.Analyze();

        //Basically, extract actions from the analysis, and compare with the type list we have to resolve parameters and populate the CustomAttributes list.
    }
}