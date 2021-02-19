using System.Diagnostics;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions.Important
{
    /// <summary>
    /// Used for error-checking, doesn't generate any pseudocode or IL
    /// </summary>
    public class AllocateInstanceAction : BaseAction
    {
        public TypeReference? TypeCreated;
        public LocalDefinition? LocalReturned;
        
        public AllocateInstanceAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var constant = !LibCpp2IlMain.ThePe!.is32Bit ? context.GetConstantInReg("rcx") : context.Stack.Peek() as ConstantDefinition;
            if (constant == null || !typeof(TypeReference).IsAssignableFrom(constant.Type)) return;

            TypeCreated = (TypeReference) constant.Value;

            LocalReturned = context.MakeLocal(TypeCreated, reg: "rax");

            if (LibCpp2IlMain.ThePe.is32Bit)
                context.Stack.Pop(); //Pop off the type created
        }

        internal AllocateInstanceAction(MethodAnalysis context, Instruction instruction, TypeDefinition typeCreated) : base(context, instruction)
        {
            //For use with struct creation only
            Debug.Assert(typeCreated.IsValueType);
            
            //Ignore the type, we're overriding it
            TypeCreated = typeCreated;
            
            LocalReturned = context.MakeLocal(TypeCreated, reg: "rax");
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string ToPsuedoCode()
        {
            return $"{TypeCreated?.FullName} {LocalReturned?.Name} = new {TypeCreated?.FullName}()";
        }

        public override string ToTextSummary()
        {
            return $"[!] Allocates an instance of type {TypeCreated} and stores it as {LocalReturned?.Name} in rax.\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}