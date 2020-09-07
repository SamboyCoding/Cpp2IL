using System.IO;
using System.Linq;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL
{
    public static class LibCpp2IlMain
    {
        public class LibCpp2IlSettings
        {
            public bool AllowManualMetadataAndCodeRegInput;
        }

        public static readonly LibCpp2IlSettings Settings = new LibCpp2IlSettings();
        
        public static float MetadataVersion = 24f;
        public static PE.PE? ThePe;
        public static Il2CppMetadata? TheMetadata;

        public static string? GetLiteralByAddress(ulong address)
        {
            var literal = LibCpp2IlGlobalMapper.Literals.FirstOrDefault(lit => lit.Offset == address);
            return literal.Offset == address ? literal.Name : null;
        }
        
        public static Il2CppTypeReflectionData? GetTypeGlobalByAddress(ulong address)
        {
            if (TheMetadata == null) return null;
            
            var typeGlobal = LibCpp2IlGlobalMapper.TypeRefs.FirstOrDefault(lit => lit.Offset == address);
            if (typeGlobal.Offset != address) return null;

            if (typeGlobal.Value is Il2CppTypeReflectionData reflectionData)
            {
                return reflectionData;
            }

            return null;
        }
        
        public static Il2CppFieldDefinition? GetFieldGlobalByAddress(ulong address)
        {
            if (TheMetadata == null) return null;
            
            var typeGlobal = LibCpp2IlGlobalMapper.FieldRefs.FirstOrDefault(lit => lit.Offset == address);
            if (typeGlobal.Offset != address) return null;

            if (typeGlobal.Value is Il2CppFieldDefinition fieldDefinition)
            {
                return fieldDefinition;
            }

            return null;
        }
        
        public static GlobalIdentifier? GetMethodGlobalByAddress(ulong address)
        {
            if (TheMetadata == null) return null;
            
            var methodGlobal = LibCpp2IlGlobalMapper.MethodRefs.FirstOrDefault(lit => lit.Offset == address);

            return methodGlobal;
        }

        public static Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address)
        {
            var global = GetMethodGlobalByAddress(address);
            if (!global.HasValue) return null;
            
            if (global.Value.Offset != address) return null;

            if (global.Value.Value is Il2CppGlobalGenericMethodRef genericMethodRef)
            {
                //TODO: This isn't quite right, there should be a way to get the specific generic reference here for specific method pointers.
                return genericMethodRef.baseMethod;
            }

            if (global.Value.Value is Il2CppMethodDefinition methodDefinition)
            {
                return methodDefinition;
            }

            //Nasty fallback but we shouldn't ever get here.
            return TheMetadata!.methodDefs.FirstOrDefault(type => type.GlobalKey == global.Value.Name);
        }

        /// <summary>
        /// Initialize the metadata and PE from a pair of byte arrays.
        /// </summary>
        /// <param name="peBytes">The content of the GameAssembly.dll file.</param>
        /// <param name="metadataBytes">The content of the global-metadata.dat file</param>
        /// <param name="unityVersion">The unity version, split on periods, with the patch version (e.g. f1) stripped out. For example, [2018, 2, 0]</param>
        /// <returns>True if the initialize succeeded, else false</returns>
        /// <throws><see cref="System.FormatException"/> if the metadata is invalid (bad magic number, bad version), or if the PE is invalid (bad header signature, bad magic number)<br/></throws>
        /// <throws><see cref="System.NotSupportedException"/> if the PE file specifies it is neither for AMD64 or i386 architecture</throws>
        public static bool Initialize(byte[] peBytes, byte[] metadataBytes, int[] unityVersion)
        {
            TheMetadata = Il2CppMetadata.ReadFrom(metadataBytes, unityVersion);

            if (TheMetadata == null)
                return false;

            ThePe = new PE.PE(new MemoryStream(peBytes, 0, peBytes.Length, false, true), TheMetadata.maxMetadataUsages);
            if (!ThePe.PlusSearch(TheMetadata.methodDefs.Count(x => x.methodIndex >= 0), TheMetadata.typeDefs.Length))
                return false;
            
            LibCpp2IlGlobalMapper.MapGlobalIdentifiers(TheMetadata, ThePe);

            return true;
        }

        /// <summary>
        /// Initialize the metadata and PE from their respective file locations.
        /// </summary>
        /// <param name="pePath">The path to the GameAssembly.dll file</param>
        /// <param name="metadataPath">The path to the global-metadata.dat file</param>
        /// <param name="unityVersion">The unity version, split on periods, with the patch version (e.g. f1) stripped out. For example, [2018, 2, 0]</param>
        /// <returns>True if the initialize succeeded, else false</returns>
        /// <throws><see cref="System.FormatException"/> if the metadata is invalid (bad magic number, bad version), or if the PE is invalid (bad header signature, bad magic number)<br/></throws>
        /// <throws><see cref="System.NotSupportedException"/> if the PE file specifies it is neither for AMD64 or i386 architecture</throws>
        public static bool LoadFromFile(string pePath, string metadataPath, int[] unityVersion)
        {
            var metadataBytes = File.ReadAllBytes(metadataPath);
            var peBytes = File.ReadAllBytes(pePath);

            return Initialize(peBytes, metadataBytes, unityVersion);
        }
    }
}