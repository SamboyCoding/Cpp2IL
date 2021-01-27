using System.Diagnostics;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis.Actions
{
    /// <summary>
    /// Used for error-checking, doesn't generate any pseudocode or IL
    /// </summary>
    public class AllocateInstanceAction : BaseAction
    {
        public TypeDefinition? TypeCreated;
        public LocalDefinition? LocalReturned;
        
        public AllocateInstanceAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var constant = !LibCpp2IlMain.ThePe!.is32Bit ? context.GetConstantInReg("rcx") : context.Stack.Peek() as ConstantDefinition;
            if (constant == null || constant.Type != typeof(TypeDefinition)) return;

            TypeCreated = (TypeDefinition) constant.Value;

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