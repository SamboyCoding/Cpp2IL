using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class GlobalFieldDefToConstantAction : BaseAction
    {
        public readonly Il2CppFieldDefinition? FieldData;
        private readonly FieldDefinition? ResolvedField;
        private ConstantDefinition? ConstantWritten;

        public GlobalFieldDefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = LibCpp2IlMain.Binary.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            FieldData = LibCpp2IlMain.GetFieldGlobalByAddress(globalAddress);
            ResolvedField = SharedState.UnmanagedToManagedFields[FieldData];

            var destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            var name = ResolvedField.Name;
            
            ConstantWritten = context.MakeConstant(typeof(FieldDefinition), ResolvedField, name, destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the type definition for managed field {ResolvedField!.FullName} as a constant \"{ConstantWritten?.Name}\"";
        }
    }
}