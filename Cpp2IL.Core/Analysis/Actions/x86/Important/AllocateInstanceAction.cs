using System.Diagnostics;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class AllocateInstanceAction : AbstractNewObjAction<Instruction>
    {
        public AllocateInstanceAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var constant = !LibCpp2IlMain.Binary!.is32Bit ? context.GetConstantInReg("rcx") : context.Stack.Peek() as ConstantDefinition;
            if (constant == null || !typeof(TypeReference).IsAssignableFrom(constant.Type)) return;

            TypeCreated = (TypeReference) constant.Value;

            LocalReturned = context.MakeLocal(TypeCreated, reg: "rax");
            
            //Keeping this as used implicitly because we have to create instances of things.
            RegisterUsedLocal(LocalReturned);

            if (LibCpp2IlMain.Binary.is32Bit)
                context.Stack.Pop(); //Pop off the type created
        }

        internal AllocateInstanceAction(MethodAnalysis<Instruction> context, Instruction instruction, TypeDefinition typeCreated) : base(context, instruction)
        {
            //For use with struct creation only
            Debug.Assert(typeCreated.IsValueType);
            
            //Ignore the type, we're overriding it
            TypeCreated = typeCreated;
            
            LocalReturned = context.MakeLocal(TypeCreated, reg: "rax");
        }
    }
}