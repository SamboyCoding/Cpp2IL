using System;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.CorePlugin;

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
            
            var assemblyName = assembly.Definition.AssemblyName.Name;
            MiscUtils.InvalidPathChars.ForEach(c => assemblyName = assemblyName.Replace(c, '_'));
            
            var assemblyNameClean = MiscUtils.InvalidPathElements.Contains(assemblyName) ? $"__invalidwin32name_{assemblyName}__" : assemblyName;

            MiscUtils.ExecuteParallel(assembly.Types, type =>
            {
                if (type is InjectedTypeAnalysisContext)
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

                        typeDump.AppendLine();
                    }
                    catch (Exception e)
                    {
                        typeDump.Append("Method threw an exception while analyzing - ").AppendLine(e.Message).AppendLine();
                    }
                }

                var namespaceSplit = type.Namespace.Split('.');
                namespaceSplit = namespaceSplit
                    .Peek(n => MiscUtils.InvalidPathChars.ForEach(c => n = n.Replace(c, '_')))
                    .Select(n => MiscUtils.InvalidPathElements.Contains(n) ? $"__illegalwin32name_{n}__" : n)
                    .ToArray();

                var directory = Path.Combine(new[] {outputRoot, assemblyNameClean}.Concat(namespaceSplit).ToArray());
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var typeName = type.Name;
                if(type.Definition.DeclaringType != null)
                    typeName = type.Definition.DeclaringType.Name + '_' + typeName;
                
                MiscUtils.InvalidPathChars.ForEach(c => typeName = typeName.Replace(c, '_'));

                var file = Path.Combine(directory, $"{typeName}.txt");
                File.WriteAllText(file, typeDump.ToString());
            });
        }
    }
}