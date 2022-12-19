using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ProcessingLayers;

public class DeobfuscationMapProcessingLayer : Cpp2IlProcessingLayer
{
    private static bool _logUnknownTypes;
    private static int _numUnknownTypes;

    public override string Name => "Deobfuscation Map";
    public override string Id => "deobfmap";
    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var mapPath = appContext.GetExtraData<string>("deobf-map-path");
        _logUnknownTypes = appContext.GetExtraData<string>("deobf-map-log-unknown-types") != null;

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
        else if (File.Exists(mapPath))
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
        _numUnknownTypes = 0;

        var typeLines = lines.Where(t => !t.Contains("::"));
        var memberLines = lines.Where(t => t.Contains("::"));

        Logger.InfoNewline($"Applying deobfuscation map ({lines.Length} entries)...", "DeobfuscationMapProcessingLayer");
        foreach (var line in typeLines)
            ProcessLine(appContext, line);

        foreach (var line in memberLines)
            ProcessLine(appContext, line);

        if (!_logUnknownTypes)
        {
            //Print a summary if we didn't log each individually.
            Logger.WarnNewline($"Encountered {_numUnknownTypes} unknown types in deobf map. Add the configuration option deobf-map-log-unknown-types=yes to log them.", "DeobfuscationMapProcessingLayer");
        }
    }

    private static void ProcessLine(ApplicationAnalysisContext appContext, string line)
    {
        //Obfuscated;deobfuscated[;priority]
        var split = line.Split(';');

        if (split.Length < 2)
            return;

        var (obfuscated, deobfuscated) = (split[0], split[1]);

        ProcessRemapping(appContext, obfuscated, deobfuscated);
    }

    private static void ProcessRemapping(ApplicationAnalysisContext appContext, string obfuscated, string deobfuscated)
    {
        if (obfuscated.Contains("::"))
        {
            var index = obfuscated.IndexOf("::", StringComparison.Ordinal);
            var typeName = obfuscated[..index];
            var memberName = obfuscated[(index + 2)..];

            var type = GetTypeByObfName(appContext, typeName);

            var member = type?.Fields.FirstOrDefault(f => f.Name == memberName);
            if (member == null)
            {
                //TODO Non-fields? Currently only used for enums
                return;
            }

            member.OverrideName = deobfuscated;
            return;
        }

        var matchingType = GetTypeByObfName(appContext, obfuscated);

        if (matchingType == null)
            return;

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

    private static TypeAnalysisContext? GetTypeByObfName(ApplicationAnalysisContext appContext, string obfuscated)
    {
        if (obfuscated.StartsWith("."))
            //If there's no namespace, strip the leading dot
            obfuscated = obfuscated[1..];

        //Create another copy of obfuscated name with last . replaced with a /, for subclasses
        var lastDot = obfuscated.LastIndexOf('.');
        var withSlash = obfuscated;
        if (lastDot > 0)
            withSlash = obfuscated[..lastDot] + "/" + obfuscated[(lastDot + 1)..];

        //TODO Change this to a dict at some point and recalculate between type and member names.
        var matchingType = appContext.AllTypes.AsParallel().FirstOrDefault(t => t.FullName == obfuscated || t.FullName == withSlash);

        if (matchingType == null)
        {
            if (_logUnknownTypes)
                Logger.WarnNewline("Could not find type " + obfuscated, "DeobfuscationMapProcessingLayer");
            else
                _numUnknownTypes++;
            return null;
        }

        return matchingType;
    }
}