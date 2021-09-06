using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class PushGlobalAction : BaseAction<Instruction>
    {
        private object _theUsage;

        public PushGlobalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var offset = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64;
            MetadataUsage? usage;
            if (LibCpp2IlMain.GetAnyGlobalByAddress(offset) is { } globalIdentifier)
            {
                _theUsage = globalIdentifier;
                usage = globalIdentifier;
            }
            else
            {
                _theUsage = new UnknownGlobalAddr(offset);
                return;
            }

            if (usage.Offset != offset) return;

            switch (usage.Type)
            {
                case MetadataUsageType.Type:
                case MetadataUsageType.TypeInfo:
                    var typeDefinition = Utils.TryResolveTypeReflectionData(usage.AsType());
                    context.Stack.Push(context.MakeConstant(typeof(TypeDefinition), typeDefinition));
                    break;
                case MetadataUsageType.MethodDef:
                    var methodDefinition = SharedState.UnmanagedToManagedMethods[usage.AsMethod()];
                    context.Stack.Push(context.MakeConstant(typeof(MethodDefinition), methodDefinition));
                    break;
                case MetadataUsageType.MethodRef:
                    var unmanagedReference = usage.AsGenericMethodRef();
                    
                    var managedMethodRef = unmanagedReference.baseMethod.AsManaged() as MethodReference;

                    if (unmanagedReference.methodGenericParams.Length > 0)
                    {
                        var methodGParams = unmanagedReference.methodGenericParams
                            .Select(data => Utils.TryResolveTypeReflectionData(data, managedMethodRef))
                            .ToList();

                        if (methodGParams.Any(g => g == null))
                            break;

                        var gim = new GenericInstanceMethod(managedMethodRef);
                        methodGParams.ForEach(gim.GenericArguments.Add);
                        managedMethodRef = gim;
                    }

                    var managedTypeRef = SharedState.UnmanagedToManagedTypes[unmanagedReference.declaringType] as TypeReference;
                    if (unmanagedReference.typeGenericParams.Length > 0)
                    {
                        var typeGParams = unmanagedReference.typeGenericParams
                            .Select(data => Utils.TryResolveTypeReflectionData(data, managedTypeRef))
                            .ToList();

                        if (typeGParams.Any(g => g == null))
                            break;

                        var git = new GenericInstanceType(managedTypeRef);
                        typeGParams.ForEach(git.GenericArguments.Add);
                        managedTypeRef = git;
                    }
                    
                    context.Stack.Push(context.MakeConstant(typeof(GenericMethodReference), new GenericMethodReference(managedTypeRef, managedMethodRef)));
                    break;
                case MetadataUsageType.FieldInfo:
                    var fieldDefinition = SharedState.UnmanagedToManagedFields[usage.AsField()];
                    context.Stack.Push(context.MakeConstant(typeof(FieldDefinition), fieldDefinition));
                    break;
                case MetadataUsageType.StringLiteral:
                    context.Stack.Push(context.MakeConstant(typeof(string), usage.AsLiteral()));
                    break;
            }
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
            return $"Pushes {_theUsage} onto the stack";
        }
    }
}