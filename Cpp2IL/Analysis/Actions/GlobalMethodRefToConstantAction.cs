using System;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Iced.Intel;
using SharpDisasm.Udis86;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalMethodRefToConstantAction : BaseAction
    {
        public Il2CppMethodDefinition? MethodData;
        public MethodDefinition? ResolvedMethod;
        public ConstantDefinition ConstantWritten;
        
        public GlobalMethodRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = LibCpp2IlMain.ThePe.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            MethodData = LibCpp2IlMain.GetMethodDefinitionByGlobalAddress(globalAddress);
            var (type, genericParams) = Utils.TryLookupTypeDefByName(MethodData!.DeclaringType.FullName);

            if (type == null)
            {
                Console.WriteLine("Failed to lookup managed type for declaring type of " + MethodData.GlobalKey + ", which is " + MethodData.DeclaringType.FullName);
                return;
            }
            
            ResolvedMethod = type.Methods.FirstOrDefault(m => m.Name == MethodData.Name);

            if (ResolvedMethod == null) return;
            
            var destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            var name = ResolvedMethod.Name;
            
            ConstantWritten = context.MakeConstant(typeof(MethodDefinition), ResolvedMethod, name, destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the type definition for managed method {ResolvedMethod!.FullName} as a constant \"{ConstantWritten.Name}\"";
        }
    }
}