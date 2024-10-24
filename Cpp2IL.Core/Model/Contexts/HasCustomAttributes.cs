using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.CustomAttributes;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// A base class to represent any type which has, or can have, custom attributes.
/// </summary>
public abstract class HasCustomAttributes(uint token, ApplicationAnalysisContext appContext)
    : HasToken(token, appContext)
{
    private bool _hasAnalyzedCustomAttributeData;

    /// <summary>
    /// On V29, stores the custom attribute blob. Pre-29, stores the bytes for the custom attribute generator function.
    /// </summary>
    public Memory<byte> RawIl2CppCustomAttributeData = Memory<byte>.Empty;

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
    public abstract AssemblyAnalysisContext CustomAttributeAssembly { get; }

    public abstract string CustomAttributeOwnerName { get; }

    public bool IsCompilerGeneratedBasedOnCustomAttributes =>
        CustomAttributes?.Any(a => a.Constructor.DeclaringType!.FullName.Contains("CompilerGeneratedAttribute"))
        ?? AttributeTypes?.Any(t => t.Type == Il2CppTypeEnum.IL2CPP_TYPE_CLASS && t.AsClass().FullName!.Contains("CompilerGeneratedAttribute"))
        ?? false;


#pragma warning disable CS8618 //Non-null member is not initialized.
#pragma warning restore CS8618

    protected void InitCustomAttributeData()
    {
        if (AppContext.MetadataVersion >= 29)
        {
            var offsets = GetV29BlobOffsets();

            if (!offsets.HasValue)
                return;

            var (blobStart, blobEnd) = offsets.Value;
            RawIl2CppCustomAttributeData = AppContext.Metadata.ReadByteArrayAtRawAddress(blobStart, (int)(blobEnd - blobStart));

            return;
        }

        AttributeTypeRange = AppContext.Metadata.GetCustomAttributeData(CustomAttributeAssembly.Definition.Image, CustomAttributeIndex, Token, out var rangeIndex);

        if (AttributeTypeRange == null || AttributeTypeRange.count == 0)
        {
            RawIl2CppCustomAttributeData = Array.Empty<byte>();
            AttributeTypes = [];
            return; //No attributes
        }

        AttributeTypes = Enumerable.Range(AttributeTypeRange.start, AttributeTypeRange.count)
            .Select(attrIdx => LibCpp2IlMain.TheMetadata!.attributeTypes[attrIdx])
            .Select(typeIdx => LibCpp2IlMain.Binary!.GetType(typeIdx))
            .ToList();

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
            var ptrToAddress = baseAddress + (ulong)relativeIndex * AppContext.Binary.PointerSize;
            generatorPtr = AppContext.Binary.ReadPointerAtVirtualAddress(ptrToAddress);
        }

        if (generatorPtr == 0 || !AppContext.Binary.TryMapVirtualAddressToRaw(generatorPtr, out _))
        {
            Logger.WarnNewline($"Supposedly had custom attributes ({string.Join(", ", AttributeTypes)}), but generator was null for " + this, "CA Restore");
            RawIl2CppCustomAttributeData = Memory<byte>.Empty;
            return; //Possibly no attributes with params?
        }

        CaCacheGeneratorAnalysis = new(generatorPtr, AppContext, this);
        RawIl2CppCustomAttributeData = CaCacheGeneratorAnalysis.RawBytes;
    }

    private (long blobStart, long blobEnd)? GetV29BlobOffsets()
    {
        var target = new Il2CppCustomAttributeDataRange() { token = Token };
        var caIndex = AppContext.Metadata.AttributeDataRanges.BinarySearch
        (
            CustomAttributeAssembly.Definition.Image.customAttributeStart,
            (int)CustomAttributeAssembly.Definition.Image.customAttributeCount,
            target,
            new TokenComparer()
        );

        if (caIndex < 0)
        {
            RawIl2CppCustomAttributeData = Array.Empty<byte>();
            return null;
        }

        var attributeDataRange = AppContext.Metadata.AttributeDataRanges[caIndex];
        var next = AppContext.Metadata.AttributeDataRanges[caIndex + 1];

        var blobStart = AppContext.Metadata.metadataHeader.attributeDataOffset + attributeDataRange.startOffset;
        var blobEnd = AppContext.Metadata.metadataHeader.attributeDataOffset + next.startOffset;
        return (blobStart, blobEnd);
    }

    /// <summary>
    /// Attempt to parse the Il2CppCustomAttributeData blob into custom attributes.
    /// </summary>
    public void AnalyzeCustomAttributeData(bool allowAnalysis = true)
    {
        if (_hasAnalyzedCustomAttributeData)
            return;

        _hasAnalyzedCustomAttributeData = true;

        CustomAttributes = [];

        if (AppContext.MetadataVersion >= 29)
        {
            AnalyzeCustomAttributeDataV29();
            return;
        }

        if (RawIl2CppCustomAttributeData.Length == 0)
            return;

        if (allowAnalysis)
        {
            try
            {
                CaCacheGeneratorAnalysis!.Analyze();
            }
            catch (Exception e)
            {
                Logger.WarnNewline("Failed to analyze custom attribute cache generator for " + this + " because " + e.Message, "CA Restore");
                return;
            }
        }

        //Basically, extract actions from the analysis, and compare with the type list we have to resolve parameters and populate the CustomAttributes list.

        foreach (var il2CppType in AttributeTypes!) //Assert nonnull because we're pre-29 at this point
        {
            var typeDef = il2CppType.AsClass();
            var attributeTypeContext = AppContext.ResolveContextForType(typeDef) ?? throw new("Unable to find type " + typeDef.FullName);

            AnalyzedCustomAttribute attribute;
            if (attributeTypeContext.Methods.FirstOrDefault(c => c.Name == ".ctor" && c.Definition!.parameterCount == 0) is { } constructor)
            {
                attribute = new(constructor);
            }
            else if (attributeTypeContext.Methods.FirstOrDefault(c => c.Name == ".ctor") is { } anyConstructor)
            {
                //TODO change this to actual constructor w/ params once anaylsis is available
                attribute = new(anyConstructor);
            }
            else
                //No constructor - shouldn't happen?
                continue;

            //Add the attribute, even if we don't have constructor params, so it can be read regardless
            CustomAttributes.Add(attribute);
        }
    }

    /// <summary>
    /// Parses the Il2CppCustomAttributeData blob as a v29 metadata attribute blob into custom attributes.
    /// </summary>
    private void AnalyzeCustomAttributeDataV29()
    {
        if (RawIl2CppCustomAttributeData.Length == 0)
            return;

        using var blobStream = new MemoryStream(RawIl2CppCustomAttributeData.ToArray());
        var attributeCount = blobStream.ReadUnityCompressedUint();
        var constructors = V29AttributeUtils.ReadConstructors(blobStream, attributeCount, AppContext);

        //Diagnostic data
        var startOfData = blobStream.Position;
        var perAttributeStartOffsets = new Dictionary<Il2CppMethodDefinition, long>();

        CustomAttributes = [];
        foreach (var constructor in constructors)
        {
            perAttributeStartOffsets[constructor] = blobStream.Position;

            var attributeTypeContext = AppContext.ResolveContextForType(constructor.DeclaringType!) ?? throw new($"Unable to find type {constructor.DeclaringType!.FullName}");
            var attributeMethodContext = attributeTypeContext.GetMethod(constructor) ?? throw new($"Unable to find method {constructor.Name} in type {attributeTypeContext.Definition?.FullName}");

            try
            {
                CustomAttributes.Add(V29AttributeUtils.ReadAttribute(blobStream, attributeMethodContext, AppContext));
            }
            catch (Exception e)
            {
                Logger.ErrorNewline($"Failed to read attribute data for {constructor}, which has parameters {string.Join(", ", constructor.Parameters!.Select(p => p.Type))}", "CA Restore");
                Logger.ErrorNewline($"This member ({ToString()}) has {RawIl2CppCustomAttributeData.Length} bytes of data starting at 0x{GetV29BlobOffsets()!.Value.blobStart:X}", "CA Restore");
                Logger.ErrorNewline($"The post-constructor data started at 0x{startOfData:X} bytes into our blob", "CA Restore");
                Logger.ErrorNewline($"Data for this constructor started at 0x{perAttributeStartOffsets[constructor]:X} bytes into our blob, we are now 0x{blobStream.Position:X} bytes into the blob", "CA Restore");
                Logger.ErrorNewline($"The exception message was {e.Message}", "CA Restore");

                throw;
            }
        }
    }
}
