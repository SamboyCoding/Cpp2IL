using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractFieldWriteAction<T> : BaseAction<T>
    {
        public LocalDefinition? InstanceBeingSetOn;
        public FieldUtils.FieldBeingAccessedData? FieldWritten;
        protected AbstractFieldWriteAction(MethodAnalysis<T> context, T instruction) : base(context, instruction)
        {
        }

        protected abstract string? GetValueSummary();
        protected abstract string? GetValuePseudocode();
        protected abstract Instruction[] GetIlToLoadValue(MethodAnalysis<T> context, ILProcessor processor);
        
        protected void FixUpFieldRefForAnyPotentialGenericType(MethodAnalysis<T> context)
        {
            if(context.GetMethodDefinition() is not {} contextMethod)
                return;
            
            if(FieldWritten == null)
                return;
            
            if(InstanceBeingSetOn?.Type is not {} writtenOnType)
                return;

            if (writtenOnType is null or TypeDefinition {HasGenericParameters: false})
                return;

            if (writtenOnType is TypeDefinition)
                writtenOnType = writtenOnType.MakeGenericInstanceType(writtenOnType.GenericParameters.Cast<TypeReference>().ToArray());

            if (FieldWritten.ImpliedFieldLoad is { } impliedLoad)
            {
                var fieldRef = new FieldReference(impliedLoad.Name, impliedLoad.FieldType, writtenOnType);
                FieldWritten.ImpliedFieldLoad = contextMethod.Module.ImportFieldButCleanly(fieldRef);
            } else if (FieldWritten.FinalLoadInChain is { } finalLoad)
            {
                var fieldRef = new FieldReference(finalLoad.Name, finalLoad.FieldType, writtenOnType);
                FieldWritten.FinalLoadInChain = contextMethod.Module.ImportFieldButCleanly(fieldRef);
            }
        }
        
        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (InstanceBeingSetOn == null || FieldWritten == null)
                throw new TaintedInstructionException("Instance or field is null");
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            ret.AddRange(InstanceBeingSetOn.GetILToLoad(context, processor));

            var f = FieldWritten;
            while (f.NextChainLink != null)
            {
                ret.Add(processor.Create(OpCodes.Ldfld, processor.ImportReference(f.ImpliedFieldLoad!)));
                f = f.NextChainLink;
            }
            
            ret.AddRange(GetIlToLoadValue(context, processor));

            if (f.FinalLoadInChain == null)
                throw new TaintedInstructionException("Final load in chain is null");
            
            ret.Add(processor.Create(OpCodes.Stfld, processor.ImportReference(f.FinalLoadInChain)));
            
            
            return ret.ToArray();
        }
        
        public override string ToPsuedoCode()
        {
            return $"{InstanceBeingSetOn?.Name}.{FieldWritten} = {GetValuePseudocode()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets the field {FieldWritten} (Type {FieldWritten?.GetFinalType()}) on local {InstanceBeingSetOn} to the value stored in {GetValueSummary()}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}