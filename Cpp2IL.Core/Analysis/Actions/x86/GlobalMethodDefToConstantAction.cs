using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class GlobalMethodDefToConstantAction : BaseAction<Instruction>
    {
        public Il2CppMethodDefinition? MethodData;
        public MethodDefinition? ResolvedMethod;
        public ConstantDefinition? ConstantWritten;
        private string? _destReg;

        public GlobalMethodDefToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64;
            MethodData = LibCpp2IlMain.GetMethodDefinitionByGlobalAddress(globalAddress);
            var type = SharedState.UnmanagedToManagedTypes[MethodData!.DeclaringType];

            if (type == null)
            {
                Logger.WarnNewline("Failed to lookup managed type for declaring type of " + MethodData.GlobalKey + ", which is " + MethodData.DeclaringType.FullName);
                return;
            }
            
            ResolvedMethod = type.Methods.FirstOrDefault(m => m.Name == MethodData.Name);

            if (ResolvedMethod == null) return;

            if (instruction.Mnemonic != Mnemonic.Push)
            {
                _destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            }

            var name = ResolvedMethod.Name;
            
            ConstantWritten = context.MakeConstant(typeof(MethodReference), ResolvedMethod, name, _destReg);
            
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
            return $"Loads the method definition for managed method {ResolvedMethod!.FullName} as a constant \"{ConstantWritten?.Name}\"";
        }
    }
}