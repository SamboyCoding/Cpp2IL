using System;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class LookupICallAction : BaseAction<Instruction>
    {
        private string? fullMethodSignature;
        private MethodDefinition? resolvedMethod;

        public LookupICallAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var constant = is32Bit ? context.Stack.Peek() as ConstantDefinition : context.GetConstantInReg("rcx");

            if (constant == null)
                return;

            if (is32Bit)
                context.Stack.Pop(); //Remove top value from stack

            if (!(constant.Value is Il2CppString str))
                return;

            fullMethodSignature = str.ContainedString;

            var split = fullMethodSignature.Split(new[] {"::"}, StringSplitOptions.None);

            if (split.Length < 2)
                return;

            var typeName = split[0];
            var methodSignature = split[1];

            var type = Utils.TryLookupTypeDefKnownNotGeneric(typeName);

            if (type == null)
                return;

            //TODO Check args
            resolvedMethod = type.Methods.FirstOrDefault(m => methodSignature.StartsWith(m.Name));

            if (resolvedMethod == null)
                return;

            var existingConstant = context.GetConstantInReg("rax");

            //Should be an unknown global or il2cppstring
            if (!(existingConstant is {Value: UnknownGlobalAddr _} || existingConstant is {Value: Il2CppString _}))
                return;

            //Redefine as a method def.
            existingConstant.Type = typeof(MethodDefinition);
            existingConstant.Value = resolvedMethod;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Looks up unity ICall by name \"{fullMethodSignature}\" which resolves to {resolvedMethod?.FullName}";
        }
    }
}