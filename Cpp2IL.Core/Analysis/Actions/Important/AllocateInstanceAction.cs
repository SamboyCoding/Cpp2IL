using System.Diagnostics;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class AllocateInstanceAction : BaseAction
    {
        public TypeReference? TypeCreated;
        public LocalDefinition? LocalReturned;
        
        public AllocateInstanceAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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
            if (LocalReturned == null || TypeCreated == null)
                throw new TaintedInstructionException("Local being returned, or type being allocated, couldn't be determined.");
            
            var managedConstructorCall = (AbstractCallAction?) context.Actions.Skip(context.Actions.IndexOf(this)).FirstOrDefault(i => i is AbstractCallAction);

            if (managedConstructorCall == null)
                throw new TaintedInstructionException($"Cannot find the call to the constructor for instance allocation, of type {TypeCreated} (Is Value: {TypeCreated?.IsValueType})");

            //Next call should be to a constructor.
            if (managedConstructorCall.ManagedMethodBeingCalled?.Name != ".ctor")
                throw new TaintedInstructionException($"Managed method being called is {managedConstructorCall.ManagedMethodBeingCalled?.Name}, not a ctor.");

            var result = managedConstructorCall.GetILToLoadParams(context, processor, false);

            var ctorToCall = managedConstructorCall.ManagedMethodBeingCalled!;
            
            if (ctorToCall.DeclaringType != TypeCreated)
                ctorToCall = TypeCreated?.Resolve()?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == ctorToCall.Parameters.Count) ?? throw new TaintedInstructionException($"Could not resolve a constructor with {ctorToCall.Parameters.Count} parameters.");

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