using System;
using System.Collections.Generic;
using Cpp2IL.Core.Api;
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
    
    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var stableNameStemCounts = new Dictionary<string, int>();
        
        //Initial pass through, to find which can be renamed trivially
        foreach (var typeAnalysisContext in appContext.AllTypes)
        {
            if(typeAnalysisContext is InjectedTypeAnalysisContext)
                continue;
            
            var stableName = StableNameGenerator.GetStableNameForTypeIfNeeded(typeAnalysisContext, false);

            if (stableName == null)
                //No rename needed
                continue;
            
            stableNameStemCounts[stableName] = stableNameStemCounts.GetOrCreate(stableName, () => 0) + 1;
            typeAnalysisContext.OverrideName = stableName;
        }
        
        //Remove all types which got a non-unique name
        foreach (var typeAnalysisContext in appContext.AllTypes)
        {
            if(typeAnalysisContext is InjectedTypeAnalysisContext)
                continue;
            
            if(typeAnalysisContext.OverrideName == null)
                //Wasn't renamed
                continue;
            
            var dupeCount = stableNameStemCounts[typeAnalysisContext.OverrideName];
            if (dupeCount > 1)
                //Clear rename, we'll try again with methods included
                typeAnalysisContext.OverrideName = null;
        }
        
        //Second pass, including method params this time
        foreach (var typeAnalysisContext in appContext.AllTypes)
        {
            if(typeAnalysisContext is InjectedTypeAnalysisContext)
                continue;
            
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
        
        foreach (var typeAnalysisContext in appContext.AllTypes)
        {
            if(typeAnalysisContext is InjectedTypeAnalysisContext)
                continue;
            
            if (typeAnalysisContext.OverrideName == null || !stableNameStemCounts.TryGetValue(typeAnalysisContext.OverrideName, out var count))
                continue;

            if (count == 1)
                typeAnalysisContext.OverrideName += "Unique";
            else
            {
                var thisCount = stableNameRenameCount[typeAnalysisContext.OverrideName] = stableNameRenameCount.GetOrCreate(typeAnalysisContext.OverrideName, () => -1) + 1;
                typeAnalysisContext.OverrideName += thisCount;
            }
        }
    }
}