using System.Diagnostics;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
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

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (LocalReturned == null)
                throw new TaintedInstructionException();
            
            var managedConstructorCall = (CallManagedFunctionAction) context.Actions.Skip(context.Actions.IndexOf(this)).First(i => i is CallManagedFunctionAction);

            //Next call should be to a constructor.
            if (managedConstructorCall.ManagedMethodBeingCalled?.Name != ".ctor")
                throw new TaintedInstructionException();

            var result = managedConstructorCall.GetILToLoadParams(context, processor, false);

            var ctorToCall = managedConstructorCall.ManagedMethodBeingCalled!;
            
            if (ctorToCall.DeclaringType != TypeCreated)
                ctorToCall = TypeCreated?.Resolve()?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == ctorToCall.Parameters.Count) ?? throw new TaintedInstructionException();

            if (ctorToCall.HasGenericParameters && TypeCreated is GenericInstanceType git)
                ctorToCall = ctorToCall.MakeGeneric(git.GenericArguments.ToArray());

            result.Add(processor.Create(OpCodes.Newobj, ctorToCall));
            
            result.Add(processor.Create(OpCodes.Stloc, LocalReturned.Variable));

            return result.ToArray();
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