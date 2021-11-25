using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class CallMethodSpecAction : BaseX86CallAction
    {
        public CallMethodSpecAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var methodSpecConst = context.GetConstantInReg(X86Utils.GetRegisterNameNew(instruction.MemoryBase));
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

            ShouldUseCallvirt = true;

            if (methodSpec.classIndexIndex != -1)
                ManagedMethodBeingCalled = ManagedMethodBeingCalled.MakeMethodOnGenericType(methodSpec.GenericClassParams.Select(p => MiscUtils.TryResolveTypeReflectionData(p, ManagedMethodBeingCalled, context.GetMethodDefinition())).ToArray()!);

            CreateLocalForReturnType(context);
            CacheMethodInfoArg(context);
            RegisterLocals(context);
        }
    }
}