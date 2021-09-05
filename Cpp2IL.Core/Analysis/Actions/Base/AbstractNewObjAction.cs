using System.Linq;
using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractNewObjAction<T> : BaseAction<T>
    {
        public TypeReference? TypeCreated;
        public LocalDefinition? LocalReturned;
        
        protected AbstractNewObjAction(MethodAnalysis context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }
        
        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (LocalReturned == null || TypeCreated == null)
                throw new TaintedInstructionException("Local being returned, or type being allocated, couldn't be determined.");
            
            //TODO Once we abstract out MethodAnalysis somehow, fix this.
            //This is super ugly until that point.
            var thisAsX86Action = (BaseAction<Instruction>)(object)this;
            
            var managedConstructorCall = (BaseX86CallAction?) context.Actions.Skip(context.Actions.IndexOf(thisAsX86Action)).FirstOrDefault(i => i is BaseX86CallAction);

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

            result.Add(processor.Create(OpCodes.Newobj, processor.ImportReference(ctorToCall)));
            
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