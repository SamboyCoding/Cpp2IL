using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.Important;
using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public class RenameLocalsPostProcessor : PostProcessor<Instruction> {

        public override void PostProcess(MethodAnalysis<Instruction> analysis, MethodDefinition definition)
        {
            var countDict = new Dictionary<string, int>();
            
            foreach (var action in analysis.Actions)
            {
                LocalDefinition localDefinition;
                string nameBase;
                if (action is FieldToLocalAction {FieldRead: {}, LocalWritten: {}} ftla)
                {
                    var field = ftla.FieldRead.GetLast().FinalLoadInChain!;
                    nameBase = field.Name;
                    if (nameBase.Length < 4)
                        nameBase = field.FieldType.Name;

                    localDefinition = ftla.LocalWritten;
                }
                else if (action is StaticFieldToRegAction {FieldRead: {}, LocalWritten: {}} sftra)
                {
                    var field = sftra.FieldRead;
                    nameBase = field.Name;
                    if (nameBase.Length < 4)
                        nameBase = field.FieldType.Name;

                    localDefinition = sftra.LocalWritten;
                }
                else if (action is BaseX86CallAction {ReturnedLocal: { }, ManagedMethodBeingCalled: {}} aca)
                {
                    if (aca.ManagedMethodBeingCalled.Name.StartsWith("get_"))
                        nameBase = aca.ManagedMethodBeingCalled.Name[4..];
                    else if (aca.ManagedMethodBeingCalled.Name.Length > 3 && aca.ManagedMethodBeingCalled.Name.ToLower().StartsWith("get"))
                        nameBase = aca.ManagedMethodBeingCalled.Name[3..];
                    else if (aca.ManagedMethodBeingCalled.Name.ToLower().StartsWith("is"))
                        nameBase = aca.ManagedMethodBeingCalled.Name;
                    else
                        nameBase = aca.ReturnedLocal.Type!.Name;

                    localDefinition = aca.ReturnedLocal;
                }
                else if (action is ArrayLengthPropertyToLocalAction {LocalMade: { }, TheArray: { }} alptla)
                {
                    nameBase = "length";
                    localDefinition = alptla.LocalMade;
                } else if (action is ArrayElementReadToRegAction {LocalMade: { }} aertpa)
                {
                    nameBase = aertpa.LocalMade.Type!.Name;
                    localDefinition = aertpa.LocalMade;
                } else if (action is AllocateInstanceAction {LocalReturned: { }, TypeCreated: { }} aia)
                {
                    nameBase = aia.TypeCreated.Name;
                    localDefinition = aia.LocalReturned;
                }
                else
                    continue;
                
                //lower first character
                nameBase = $"{char.ToLower(nameBase[0])}{nameBase[1..]}";

                if (nameBase.Contains("`"))
                    nameBase = nameBase[..nameBase.IndexOf("`", StringComparison.Ordinal)];

                if (nameBase.EndsWith("[]"))
                    nameBase = nameBase[..^2] + "Array";

                if (!countDict.ContainsKey(nameBase))
                {
                    countDict[nameBase] = 1;
                }
                else
                {
                    countDict[nameBase]++;
                    nameBase += countDict[nameBase];
                }

                localDefinition.Name = nameBase;
            }
        }
    }
}