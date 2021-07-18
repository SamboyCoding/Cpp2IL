using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class InitializeRuntimeMetadataAction : BaseAction
    {
        private object? _metadataUsage;

        public InitializeRuntimeMetadataAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            ConstantDefinition? consDef;
            if (LibCpp2IlMain.Binary!.is32Bit)
            {
                consDef = context.Stack.Count > 0 ? context.Stack.Peek() as ConstantDefinition : null;
                if (consDef != null)
                    context.Stack.Pop();
            }
            else
                consDef = context.GetConstantInReg("rcx");

            if (consDef?.Value is TypeReference || consDef?.Value is MethodReference || consDef?.Value is FieldDefinition || consDef?.Value is string)
            {
                _metadataUsage = consDef.Value;
                
                context.SetRegContent("rax", consDef);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Initializes runtime metadata value {_metadataUsage}";
        }
    }
}