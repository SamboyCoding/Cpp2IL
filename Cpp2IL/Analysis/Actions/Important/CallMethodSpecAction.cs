using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.PE;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallMethodSpecAction : AbstractCallAction
    {
        public CallMethodSpecAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var methodSpecConst = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            var methodSpec = methodSpecConst?.Value as Il2CppMethodSpec;

            if (methodSpec?.MethodDefinition == null)
                return;

            //todo params and return type
            if (!MethodUtils.CheckParameters(instruction, methodSpec.MethodDefinition, context, !methodSpec.MethodDefinition.IsStatic, out Arguments, null, false))
                AddComment("Parameter mismatch!");

            InstanceBeingCalledOn = methodSpec.MethodDefinition?.IsStatic == true ? null : context.GetLocalInReg("rcx");

            if (methodSpec.MethodDefinition == null)
                return;

            ManagedMethodBeingCalled = SharedState.UnmanagedToManagedMethods[methodSpec.MethodDefinition];

            if (methodSpec.classIndexIndex != -1)
                ManagedMethodBeingCalled = ManagedMethodBeingCalled.MakeGeneric(methodSpec.GenericClassParams.Select(p => Utils.TryResolveTypeReflectionData(p, ManagedMethodBeingCalled)).ToArray()!);

            CreateLocalForReturnType(context);
            RegisterLocals();
        }
    }
}