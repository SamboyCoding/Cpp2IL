using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractFieldReadAction<T> : BaseAction<T>
    {
        public FieldUtils.FieldBeingAccessedData? FieldRead;
        public LocalDefinition? LocalWritten;
        protected LocalDefinition? ReadFrom;
        protected TypeReference? ReadFromType;

        protected AbstractFieldReadAction(MethodAnalysis<T> context, T instruction) : base(context, instruction)
        {
        }

        protected void FixUpFieldRefForAnyPotentialGenericType(MethodAnalysis<T> context)
        {
            if(context.GetMethodDefinition() is not {} contextMethod)
                return;
            
            if(FieldRead == null)
                return;

            if (ReadFromType is null or TypeDefinition {HasGenericParameters: false})
                return;

            if (ReadFromType is TypeDefinition)
                ReadFromType = ReadFromType.MakeGenericInstanceType(ReadFromType.GenericParameters.Cast<TypeReference>().ToArray());

            if (FieldRead.ImpliedFieldLoad is { } impliedLoad)
            {
                var fieldRef = new FieldReference(impliedLoad.Name, impliedLoad.FieldType, ReadFromType);
                FieldRead.ImpliedFieldLoad = contextMethod.Module.ImportFieldButCleanly(fieldRef);
            } else if (FieldRead.FinalLoadInChain is { } finalLoad)
            {
                var fieldRef = new FieldReference(finalLoad.Name, finalLoad.FieldType, ReadFromType);
                FieldRead.FinalLoadInChain = contextMethod.Module.ImportFieldButCleanly(fieldRef);
            }
        }
        
        public override string ToPsuedoCode()
        {
            return $"{LocalWritten?.Type?.FullName} {LocalWritten?.Name} = {ReadFrom?.Name}.{FieldRead}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads field {FieldRead} from {ReadFrom} and stores in a new local {LocalWritten}\n";
        }
        
        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (LocalWritten == null || ReadFrom == null || FieldRead == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Instruction>();

            //Load object
            ret.AddRange(ReadFrom.GetILToLoad(context, processor));

            //Access field
            ret.AddRange(FieldRead.GetILToLoad(processor));

            //Store to local
            ret.Add(processor.Create(OpCodes.Stloc, LocalWritten.Variable));
            
            return ret.ToArray();
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}