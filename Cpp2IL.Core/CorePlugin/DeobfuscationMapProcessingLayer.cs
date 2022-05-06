using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.CorePlugin;

public class DeobfuscationMapProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Deobfuscation Map";
    public override string Id => "deobfmap";
    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var mapPath = appContext.GetExtraData<string>("deobf-map-path");
        if (mapPath == null)
        {
            Logger.WarnNewline("No deobfuscation map specified - processor will not run. You need to provide the deobf-map-path, either by programmatically adding it as extra data in the app context, or by specifying it in the --processor-config command line option.", "DeobfuscationMapProcessingLayer");
            return;
        }

        byte[] deobfMap;
        if (mapPath.StartsWith("http://") || mapPath.StartsWith("https://"))
        {
            try
            {
                Logger.InfoNewline($"Downloading deobfuscation map from {mapPath}...", "DeobfuscationMapProcessingLayer");
                //Blocking call, but it's fine. All of cpp2il is blocking.
                deobfMap = new HttpClient().GetByteArrayAsync(mapPath).Result;
            }
            catch (Exception e)
            {
                Logger.ErrorNewline($"Could not download remote deobfuscation map from {mapPath}: {e.Message}. Deobfuscation will not run.", "DeobfuscationMapProcessingLayer");
                return;
            }
        }
        else if(File.Exists(mapPath))
        {
            deobfMap = File.ReadAllBytes(mapPath);
        }
        else
        {
            Logger.ErrorNewline($"File not found: {Path.GetFullPath(mapPath)}. Deobfuscation will not run.", "DeobfuscationMapProcessingLayer");
            return;
        }

        Logger.InfoNewline("Parsing deobfuscation map...", "DeobfuscationMapProcessingLayer");
        
        string deobfMapContent;
        //Check if gzipped.
        if (deobfMap.Length > 2 && deobfMap[0] == 0x1F && deobfMap[1] == 0x8B)
        {
            using var ms = new MemoryStream(deobfMap);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            deobfMapContent = reader.ReadToEnd();
        }
        else
        {
            deobfMapContent = Encoding.UTF8.GetString(deobfMap);
        }
        
        Deobfuscate(appContext, deobfMapContent);
    }

    private static void Deobfuscate(ApplicationAnalysisContext appContext, string deobfMap)
    {
        var lines = deobfMap.Split('\n');

        Logger.InfoNewline($"Applying deobfuscation map ({lines.Length} entries)...", "DeobfuscationMapProcessingLayer");
        foreach (var line in lines)
        {
            //Obfuscated;deobfuscated[;priority]
            var split = line.Split(';');
            
            if(split.Length < 2)
                continue;
            
            var (obfuscated, deobfuscated) = (split[0], split[1]);

            ProcessLine(appContext, obfuscated, deobfuscated);
        }
    }

    private static void ProcessLine(ApplicationAnalysisContext appContext, string obfuscated, string deobfuscated)
    {
        if(obfuscated.Contains("::"))
            return; //TODO Support member deobfuscation
        
        if (obfuscated.StartsWith("."))
            //If there's no namespace, strip the leading dot
            obfuscated = obfuscated[1..];

        var matchingType = appContext.AllTypes.FirstOrDefault(t => t.FullName == obfuscated);

        if (matchingType == null)
        {
            Logger.WarnNewline("Could not find type " + obfuscated, "DeobfuscationMapProcessingLayer");
            return;
        }

        //The way the rename maps work is something like this:
        //  If the obfuscated name was a nested type, the deobfuscated name is just the new name of the nested type
        //  If the obfuscated name was a top-level type, the deobfuscated name is the new namespace + . + new name

        // var originalName = matchingType.FullName;
        
        if (matchingType.DeclaringType != null)
        {
            matchingType.OverrideName = deobfuscated;
            // Logger.VerboseNewline($"Renamed nested type {originalName} to {matchingType.FullName}", "DeobfuscationMapProcessingLayer");
            return;
        }

        var lastDot = deobfuscated.LastIndexOf('.');

        if (lastDot != -1)
        {
            var namespaceName = deobfuscated[..lastDot];
            var typeName = deobfuscated[(lastDot + 1)..];

            matchingType.OverrideNs = namespaceName;
            matchingType.OverrideName = typeName;
        }
        else
        {
            matchingType.OverrideName = deobfuscated;
        }

        // Logger.VerboseNewline($"Renamed {originalName} to {matchingType.FullName}", "DeobfuscationMapProcessingLayer");
    }
}