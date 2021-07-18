using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class PushGlobalAction : BaseAction
    {
        private object _theUsage;

        public PushGlobalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var offset = LibCpp2IlMain.Binary.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            MetadataUsage? usage;
            if (LibCpp2IlMain.GetAnyGlobalByAddress(offset) is { } globalIdentifier)
            {
                _theUsage = globalIdentifier;
                usage = globalIdentifier;
            }
            else
            {
                _theUsage = new UnknownGlobalAddr(offset);
                return;
            }

            if (usage.Offset != offset) return;

            switch (usage.Type)
            {
                case MetadataUsageType.Type:
                case MetadataUsageType.TypeInfo:
                    var typeDefinition = Utils.TryResolveTypeReflectionData(usage.AsType());
                    context.Stack.Push(context.MakeConstant(typeof(TypeDefinition), typeDefinition));
                    break;
                case MetadataUsageType.MethodDef:
                    var methodDefinition = SharedState.UnmanagedToManagedMethods[usage.AsMethod()];
                    context.Stack.Push(context.MakeConstant(typeof(MethodDefinition), methodDefinition));
                    break;
                case MetadataUsageType.FieldInfo:
                    var fieldDefinition = SharedState.UnmanagedToManagedFields[usage.AsField()];
                    context.Stack.Push(context.MakeConstant(typeof(FieldDefinition), fieldDefinition));
                    break;
                case MetadataUsageType.StringLiteral:
                    context.Stack.Push(context.MakeConstant(typeof(string), usage.AsLiteral()));
                    break;
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
            return $"Pushes {_theUsage} onto the stack";
        }
    }
}