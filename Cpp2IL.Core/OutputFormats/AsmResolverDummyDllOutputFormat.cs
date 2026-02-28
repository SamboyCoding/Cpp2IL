// #define VERBOSE_LOGGING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.PE.Builder;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.OutputFormats;

public abstract class AsmResolverDllOutputFormat : Cpp2IlOutputFormat
{
    private AssemblyDefinition? MostRecentCorLib { get; set; }

    public sealed override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        var ret = BuildAssemblies(context);

        var start = DateTime.Now;
        Logger.Verbose("Generating PE images...", "DllOutput");

        if (!Directory.Exists(outputRoot))
            Directory.CreateDirectory(outputRoot);

        //Convert assembly definitions to PE files
        var peImagesToWrite = ret
            .AsParallel()
            .Select(a => (image: a.ManifestModule!.ToPEImage(new ManagedPEImageBuilder()), name: a.ManifestModule.Name!))
            .ToList();

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        start = DateTime.Now;
        Logger.Verbose("Building and writing managed PE files to disk...", "DllOutput");

        //Save them
        var fileBuilder = new ManagedPEFileBuilder();
        foreach (var (image, name) in peImagesToWrite)
        {
            var dllPath = Path.Combine(outputRoot, name);
            fileBuilder.CreateFile(image).Write(dllPath);
        }

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");
    }

    public virtual List<AssemblyDefinition> BuildAssemblies(ApplicationAnalysisContext context)
    {
#if VERBOSE_LOGGING
        var asmCount = context.Assemblies.Count;
        var typeCount = context.AllTypes.Count();
        var methodCount = context.AllTypes.SelectMany(t => t.Methods).Count();
        var fieldCount = context.AllTypes.SelectMany(t => t.Fields).Count();
        var propertyCount = context.AllTypes.SelectMany(t => t.Properties).Count();
        var eventCount = context.AllTypes.SelectMany(t => t.Events).Count();
#endif

        //Build the stub assemblies
        var start = DateTime.Now;
#if VERBOSE_LOGGING
        Logger.Verbose($"Building stub assemblies ({asmCount} assemblies, {typeCount} types)...", "DllOutput");
#else
        Logger.Verbose($"Building stub assemblies...", "DllOutput");
#endif
        List<AssemblyDefinition> ret = BuildStubAssemblies(context);
        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        start = DateTime.Now;
        Logger.Verbose("Configuring inheritance and generics...", "DllOutput");

        Parallel.ForEach(context.Assemblies, AsmResolverAssemblyPopulator.ConfigureHierarchy);

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        //Populate them
        start = DateTime.Now;

#if VERBOSE_LOGGING
        Logger.Verbose($"Adding {fieldCount} fields, {methodCount} methods, {propertyCount} properties, and {eventCount} events (in parallel)...", "DllOutput");
#else
        Logger.Verbose($"Adding fields, methods, properties, and events (in parallel)...", "DllOutput");
#endif

        MiscUtils.ExecuteParallel(context.Assemblies, AsmResolverAssemblyPopulator.CopyDataFromIl2CppToManaged);
        MiscUtils.ExecuteParallel(context.Assemblies, AsmResolverAssemblyPopulator.AddExplicitInterfaceImplementations);

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        //Populate custom attributes
        start = DateTime.Now;
        Logger.Verbose("Adding custom attributes to all of the above...", "DllOutput");
        MiscUtils.ExecuteParallel(context.Assemblies, AsmResolverAssemblyPopulator.PopulateCustomAttributes);

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        //Fill method bodies - this should always be done last
        start = DateTime.Now;
        Logger.Verbose($"Filling method bodies (in parallel)...", "DllOutput");
        MiscUtils.ExecuteParallel(context.Assemblies, FillMethodBodies);

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        TypeDefinitionsAsmResolver.Reset();

        return ret;
    }

    protected abstract void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext);

    protected virtual void FillMethodBodies(AssemblyAnalysisContext context)
    {
        foreach (var typeContext in context.Types)
        {
            if (AsmResolverAssemblyPopulator.IsTypeContextModule(typeContext))
                continue;

#if !DEBUG
            try
#endif
            {
                foreach (var methodCtx in typeContext.Methods)
                {
                    var managedMethod = methodCtx.GetExtraData<MethodDefinition>("AsmResolverMethod") ?? throw new($"AsmResolver method not found in method analysis context for {typeContext.FullName}.{methodCtx.Name}");

                    FillMethodBody(managedMethod, methodCtx);
                }
            }
#if !DEBUG
            catch (System.Exception e)
            {
                var managedType = typeContext.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeContext.FullName}");
                throw new($"Failed to process type {managedType.FullName} (module {managedType.DeclaringModule?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {context.Name}", e);
            }
#endif
        }
    }

    private List<AssemblyDefinition> BuildStubAssemblies(ApplicationAnalysisContext context)
    {
        var corlib = context.Assemblies.First(a => a.Name == "mscorlib");
        MostRecentCorLib = BuildStubAssembly(corlib, null, null);

        // The runtime info is irrelevant because we're creating our own corlib, but AsmResolver still requires that we specify one.
        var runtimeContext = new RuntimeContext(DotNetRuntimeInfo.NetCoreApp(9, 0), (bool?)null, MostRecentCorLib);
        runtimeContext.AddAssembly(MostRecentCorLib);

        context.PutExtraData("AsmResolverRuntimeContext", runtimeContext);

        var ret = context.Assemblies
            // .AsParallel()
            .Where(a => a.Name != "mscorlib")
            .Select(a => BuildStubAssembly(a, MostRecentCorLib, runtimeContext))
            .ToList();

        ret.Add(MostRecentCorLib);
        return ret;
    }

    private static AssemblyDefinition BuildStubAssembly(AssemblyAnalysisContext assemblyContext, AssemblyDefinition? corLib, RuntimeContext? runtimeContext)
    {
        //Get the name of the assembly (= the name of the DLL without the file extension)
        //Build an AsmResolver assembly from this definition
        var ourAssembly = new AssemblyDefinition(assemblyContext.Name, assemblyContext.Version)
        {
            HashAlgorithm = (AssemblyHashAlgorithm)assemblyContext.HashAlgorithm,
            Attributes = (AssemblyAttributes)assemblyContext.Flags,
            Culture = assemblyContext.Culture,
            PublicKey = assemblyContext.PublicKey,
        };

        //Setting the corlib module allows element types in references to that assembly to be set correctly without us having to manually set them.
        var moduleName = assemblyContext.CleanAssemblyName + ".dll";

        //Use either ourself as corlib, if we are corlib, otherwise the provided one
        var managedModule = new ModuleDefinition(moduleName, corLib is not null ? new(corLib) : null);
        ourAssembly.Modules.Add(managedModule);

        runtimeContext?.AddAssembly(ourAssembly);

        foreach (var il2CppTypeDefinition in assemblyContext.TopLevelTypes)
        {
            if (il2CppTypeDefinition.Name != "<Module>")
                //We skip module because I've never come across an il2cpp assembly with any top-level functions, and it's simpler to skip it as AsmResolver adds one by default.
                managedModule.TopLevelTypes.Add(BuildStubType(il2CppTypeDefinition));
        }

        if (corLib == null)
        {
            //We *are* the corlib, so cache defs now
            TypeDefinitionsAsmResolver.CacheNeededTypeDefinitions();
        }

        //We can get issues with consumers of the API if the base type is not set correctly for value types or enums, so we set it here (as early as possible) if we can
        foreach (var assemblyContextType in assemblyContext.Types)
        {
            if (assemblyContextType.Definition is not { } def || assemblyContextType.GetExtraData<TypeDefinition>("AsmResolverType") is not { } asmResolverType)
                continue;

            if (def.IsValueType)
                asmResolverType.BaseType = managedModule.DefaultImporter.ImportType(TypeDefinitionsAsmResolver.ValueType);
            else if (def.IsEnumType)
                asmResolverType.BaseType = managedModule.DefaultImporter.ImportType(TypeDefinitionsAsmResolver.Enum);
        }

        //Store the managed assembly in the context so we can use it later.
        assemblyContext.PutExtraData("AsmResolverAssembly", ourAssembly);

        return ourAssembly;
    }

    private static TypeDefinition BuildStubType(TypeAnalysisContext typeContext)
    {
        var typeDef = typeContext.Definition;

        //Initialize an empty type definition
        var ret = new TypeDefinition(typeContext.Namespace, typeContext.Name, (TypeAttributes)typeContext.Attributes);

        //Set up its layout
        if (typeDef != null)
            ConfigureTypeSize(typeDef, ret);

        //Create nested types
        foreach (var cppNestedType in typeContext.NestedTypes)
            ret.NestedTypes.Add(BuildStubType(cppNestedType));

        //Associate this asm resolve td with the type context
        typeContext.PutExtraData("AsmResolverType", ret);

        //Add to the lookup-by-id table used by the resolver
        if (typeDef != null)
            AsmResolverUtils.TypeDefsByIndex[typeDef.TypeIndex] = ret;

        return ret;
    }

    private static void ConfigureTypeSize(Il2CppTypeDefinition il2CppDefinition, TypeDefinition asmResolverDefinition)
    {
        if (!il2CppDefinition.IsValueType || il2CppDefinition.IsEnumType)
            return; // Only structs can have their layout changed

        ushort packingSize = 0;
        var classSize = 0U;
        if (!il2CppDefinition.PackingSizeIsDefault)
            packingSize = (ushort)il2CppDefinition.PackingSize;

        if (!il2CppDefinition.ClassSizeIsDefault)
        {
            if (il2CppDefinition.Size > 1 << 30)
                throw new Exception($"Got invalid size for type {il2CppDefinition}: {il2CppDefinition.RawSizes}");

            if (il2CppDefinition.Size != -1)
                classSize = (uint)il2CppDefinition.Size;
            else
                classSize = 0; //Not sure what this value actually implies but it seems to work
        }

        if (packingSize != 0 || classSize != 0)
            asmResolverDefinition.ClassLayout = new(packingSize, classSize);
    }
}
