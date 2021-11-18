using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class GlobalFieldDefToConstantAction : BaseAction<Instruction>
    {
        public readonly Il2CppFieldDefinition? FieldData;
        private readonly FieldDefinition? ResolvedField;
        private ConstantDefinition? ConstantWritten;
        private string _destReg;

        public GlobalFieldDefToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64;
            FieldData = LibCpp2IlMain.GetFieldGlobalByAddress(globalAddress);
            ResolvedField = SharedState.UnmanagedToManagedFields[FieldData];

            if (instruction.Mnemonic != Mnemonic.Push)
            {
                _destReg = instruction.Op0Kind == OpKind.Register ? MiscUtils.GetRegisterNameNew(instruction.Op0Register) : null;
            }

            var name = ResolvedField.Name;
            
            ConstantWritten = context.MakeConstant(typeof(FieldDefinition), ResolvedField, name, _destReg);

            if (instruction.Mnemonic == Mnemonic.Push)
            {
                context.Stack.Push(ConstantWritten);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
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