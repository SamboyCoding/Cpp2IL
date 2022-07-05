using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using StableNameDotNet;

namespace Cpp2IL.Core.CorePlugin;

/// <summary>
/// This class is functionally adapted from parts of Il2CppAssemblyUnhollower
/// </summary>
public class StableRenamingProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Stable (Unhollower-Style) Renaming";
    public override string Id => "stablenamer";
    
    private DeobfuscationMapProcessingLayer? _deobfuscationMapProcessingLayer;

    public override void PreProcess(ApplicationAnalysisContext context, List<Cpp2IlProcessingLayer> layers)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            if (layers[i] is not DeobfuscationMapProcessingLayer deobf)
                continue;
            
            //Save this for manual invocation later
            Logger.InfoNewline("Found DeobfuscationMapProcessingLayer. It will be run at the correct time", "StableRenamingProcessingLayer");
            _deobfuscationMapProcessingLayer = deobf;
            layers.RemoveAt(i);
            return;
        }
    }

    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var stableNameStemCounts = new Dictionary<string, int>();

        var typesToProcess = appContext.AllTypes.Where(t => t is not InjectedTypeAnalysisContext).ToArray();
        
        //Initial pass through, to find types which can be renamed trivially
        foreach (var typeAnalysisContext in typesToProcess)
        {
            var stableName = StableNameGenerator.GetStableNameForTypeIfNeeded(typeAnalysisContext, false);

            if (stableName == null)
                //No rename needed
                continue;
            
            stableNameStemCounts[stableName] = stableNameStemCounts.GetOrCreate(stableName, () => 0) + 1;
            typeAnalysisContext.OverrideName = stableName;
        }
        
        StableNameGenerator.RenamedTypes.Clear();

        //Remove all types which got a non-unique name
        foreach (var typeAnalysisContext in typesToProcess)
        {
            if(typeAnalysisContext.OverrideName == null)
                //Wasn't renamed
                continue;
            
            var dupeCount = stableNameStemCounts[typeAnalysisContext.OverrideName];
            if (dupeCount > 1)
                //Clear rename, we'll try again with methods included
                typeAnalysisContext.OverrideName = null;
            else
                //Inform name generator of the rename so it can use them later
                StableNameGenerator.RenamedTypes[typeAnalysisContext] = typeAnalysisContext.OverrideName;
        }
        
        //Second pass, including method params this time
        foreach (var typeAnalysisContext in typesToProcess)
        {
            if (typeAnalysisContext.OverrideName != null)
                //Already renamed
                continue;
            
            var stableName = StableNameGenerator.GetStableNameForTypeIfNeeded(typeAnalysisContext, true);

            if (stableName == null)
                //No rename needed
                continue;
            
            stableNameStemCounts[stableName] = stableNameStemCounts.GetOrCreate(stableName, () => 0) + 1;
            typeAnalysisContext.OverrideName = stableName;
        }

        //Now we rename duplicates to add a numerical suffix, and rename non-duplicates to add a Unique suffix.
        var stableNameRenameCount = new Dictionary<string, int>();
        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            if (typeAnalysisContext.OverrideName == null || !stableNameStemCounts.TryGetValue(typeAnalysisContext.OverrideName, out var count))
                continue;
            
            //Handle generic type backtick suffixes
            string? backTickSuffix = null;
            if (typeAnalysisContext.OverrideName.Length > 2 && typeAnalysisContext.OverrideName[^2] == '`')
            {
                backTickSuffix = typeAnalysisContext.OverrideName[^2..];
                typeAnalysisContext.OverrideName = typeAnalysisContext.OverrideName[..^2];
            }

            if (count == 1)
                typeAnalysisContext.OverrideName += "Unique";
            else
            {
                var thisCount = stableNameRenameCount[typeAnalysisContext.OverrideName] = stableNameRenameCount.GetOrCreate(typeAnalysisContext.OverrideName, () => -1) + 1;
                typeAnalysisContext.OverrideName += thisCount;
            }

            if (backTickSuffix != null)
                typeAnalysisContext.OverrideName += backTickSuffix;
        }
        
        //Now rename enum values
        foreach (var type in typesToProcess)
        {
            if(!type.IsEnumType)
                continue;
            
            //All static fields
            foreach (var field in type.Fields)
            {
                if(!field.IsStatic || !field.Attributes.HasFlag(FieldAttributes.HasDefault))
                    continue;
                
                if(!StableNameGenerator.IsObfuscated(field.Name))
                    continue;

                field.OverrideName = $"EnumValue" + field.BackingData!.DefaultValue;
            }
        }
        
        //If the user wants to rename types using a deobfuscation map, do that now
        if (_deobfuscationMapProcessingLayer != null)
        {
            Logger.InfoNewline("Running Deobfuscation Map Processing Layer Now...", "StableRenamingProcessingLayer");
            _deobfuscationMapProcessingLayer.Process(appContext);
            Logger.InfoNewline("Deobfuscation Map Processing Layer Finished.", "StableRenamingProcessingLayer");
        }

        //Now (post deobf), rename all methods
        foreach (var typeAnalysisContext in typesToProcess)
        { 
            var typeMethodNames = new Dictionary<string, int>();
            foreach (var methodAnalysisContext in typeAnalysisContext.Methods)
            {
                if(methodAnalysisContext is InjectedMethodAnalysisContext)
                    continue;
                
                var stableName = StableNameGenerator.GetStableNameForMethodIfNeeded(methodAnalysisContext);

                if (stableName == null)
                    //No rename needed
                    continue;

                var occurenceCount = typeMethodNames.GetOrCreate(stableName, () => 0);
                typeMethodNames[stableName]++;
                methodAnalysisContext.OverrideName = $"{stableName}_{occurenceCount}";
                
                //If renaming a method, also rename its params
                for (var i = 0; i < methodAnalysisContext.Parameters.Count; i++)
                {
                    var param = methodAnalysisContext.Parameters[i];
                    param.OverrideName = $"param_{i}";
                }
            }
        }
        
        //Finally, rename all fields
        foreach (var typeAnalysisContext in typesToProcess)
        {
            var typeFieldNames = new Dictionary<string, int>();
            foreach (var fieldAnalysisContext in typeAnalysisContext.Fields)
            {
                if(fieldAnalysisContext is InjectedFieldAnalysisContext)
                    continue;
                
                var stableName = StableNameGenerator.GetStableNameForFieldIfNeeded(fieldAnalysisContext);

                if (stableName == null)
                    //No rename needed
                    continue;

                var occurenceCount = typeFieldNames.GetOrCreate(stableName, () => 0);
                typeFieldNames[stableName]++;
                fieldAnalysisContext.OverrideName = $"{stableName}_{occurenceCount}";
            }
        }
    }
}