using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.OutputFormats;

public class IsilDumpOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "isil";
    public override string OutputFormatName => "ISIL Dump";
    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        outputRoot = Path.Combine(outputRoot, "IsilDump");

        var numAssemblies = context.Assemblies.Count;
        var i = 0;
        foreach (var assembly in context.Assemblies)
        {
            Logger.InfoNewline($"Processing assembly {i++} of {numAssemblies}: {assembly.Definition.AssemblyName.Name}", "IsilOutputFormat");

            var assemblyNameClean = assembly.CleanAssemblyName;

            MiscUtils.ExecuteParallel(assembly.Types, type =>
            {
                if (type is InjectedTypeAnalysisContext)
                    return;
                
                if(type.Methods.Count == 0)
                    return;

                var typeDump = new StringBuilder();

                typeDump.Append("Type: ").AppendLine(type.Definition!.FullName).AppendLine();

                foreach (var method in type.Methods)
                {
                    if (method is InjectedMethodAnalysisContext)
                        continue;

                    typeDump.Append("Method: ").AppendLine(method.Definition!.HumanReadableSignature).AppendLine();

                    try
                    {

                        typeDump.AppendLine("Disassembly:");
                        typeDump.Append('\t').AppendLine(context.InstructionSet.PrintAssembly(method).Replace("\n", "\n\t"));

                        typeDump.AppendLine().AppendLine("ISIL:");
                        
                        method.Analyze();

                        if (method.ConvertedIsil == null || method.ConvertedIsil.Count == 0)
                        {
                            typeDump.AppendLine("No ISIL was generated");
                            continue;
                        }

                        foreach (var isilInsn in method.ConvertedIsil)
                        {
                            typeDump.Append('\t').Append(isilInsn).AppendLine();
                        }
                        
                        method.ReleaseAnalysisData();

                        typeDump.AppendLine();
                    }
                    catch (Exception e)
                    {
                        typeDump.Append("Method threw an exception while analyzing - ").AppendLine(e.ToString()).AppendLine();
                    }
                }

                WriteTypeDump(outputRoot, type, typeDump.ToString(), assemblyNameClean);
            });
        }
    }

    private static string GetFilePathForType(string outputRoot, TypeAnalysisContext type, string assemblyNameClean)
    {
        //Get root assembly directory
        var assemblyDir = Path.Combine(outputRoot, assemblyNameClean);

        //If type is nested, we should use namespace of ultimate declaring type, which could be an arbitrary depth
        //E.g. rewired has a type Rewired.Data.Mapping.HardwareJoystickMap, which contains a nested class Platform_Linux_Base, which contains MatchingCriteria, which contains ElementCount.
        var ultimateDeclaringType = type;
        while (ultimateDeclaringType.DeclaringType != null)
            ultimateDeclaringType = ultimateDeclaringType.DeclaringType;

        var namespaceSplit = ultimateDeclaringType.Namespace!.Split('.');
        namespaceSplit = namespaceSplit
            .Peek(n => MiscUtils.InvalidPathChars.ForEach(c => n = n.Replace(c, '_')))
            .Select(n => MiscUtils.InvalidPathElements.Contains(n) ? $"__illegalwin32name_{n}__" : n)
            .ToArray();
        
        //Ok so we have the namespace directory. Now we need to join all the declaring type hierarchy together for a filename.
        var declaringTypeHierarchy = new List<string>();
        var declaringType = type.DeclaringType;
        while (declaringType != null)
        {
            declaringTypeHierarchy.Add(declaringType.Name!);
            declaringType = declaringType.DeclaringType;
        }

        //Reverse so we have top-level type first.
        declaringTypeHierarchy.Reverse();

        //Join the hierarchy together with _NestedType_ separators
        string filename;
        if(declaringTypeHierarchy.Count > 0)
            filename = $"{string.Join("_NestedType_", declaringTypeHierarchy)}_NestedType_{type.Name}.txt";
        else
            filename = $"{type.Name}.txt";

        //Get directory from assembly root + namespace
        var directory = Path.Combine(namespaceSplit.Prepend(assemblyDir).ToArray());
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        //Clean up the filename
        MiscUtils.InvalidPathChars.ForEach(c => filename = filename.Replace(c, '_'));

        //Combine the directory and filename
        return Path.Combine(directory, filename);
    }

    private static void WriteTypeDump(string outputRoot, TypeAnalysisContext type, string typeDump, string assemblyNameClean)
    {
        var file = GetFilePathForType(outputRoot, type, assemblyNameClean);
        File.WriteAllText(file, typeDump);
    }
}
