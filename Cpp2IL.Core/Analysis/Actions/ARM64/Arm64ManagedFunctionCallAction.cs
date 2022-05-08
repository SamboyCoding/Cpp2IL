using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ManagedFunctionCallAction : AbstractCallAction<Arm64Instruction>
    {
        private readonly ulong _jumpTarget;
        private bool wasArrayInstantiation;
        private long[]? instantiatedArrayValues = null;

        public Arm64ManagedFunctionCallAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            _jumpTarget = (ulong) instruction.Details.Operands[0].Immediate;
            
            InstanceBeingCalledOn = context.GetLocalInReg("x0"); //Doesn't matter if this is a static method, the value is ignored in that case.
            
            var listOfCallableMethods = LibCpp2IlMain.GetManagedMethodImplementationsAtAddress(_jumpTarget);
            
            if (listOfCallableMethods == null)
            {
                AddComment("Could not find any callable methods. Bailing out.");
                return;
            }
            
            Il2CppMethodDefinition? possibleTarget = null;
            if (listOfCallableMethods.Count == 1)
            {
                possibleTarget = listOfCallableMethods.First();

                //TODO Update CheckParameters to handle arm64 params
                if (!MethodUtils.CheckParameters(instruction, possibleTarget, context, !possibleTarget.IsStatic, out Arguments, InstanceBeingCalledOn, failOnLeftoverArgs: false))
                    AddComment("parameters do not match, but there is only one function at this address.");

                if (!possibleTarget.IsStatic && InstanceBeingCalledOn?.Type != null && !MiscUtils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(possibleTarget.DeclaringType!), InstanceBeingCalledOn.Type))
                    AddComment($"This is an instance method, but the type of the 'this' parameter is mismatched. Expecting {possibleTarget.DeclaringType.Name}, actually {InstanceBeingCalledOn.Type.FullName}");
                else if (!possibleTarget.IsStatic && InstanceBeingCalledOn?.Type != null)
                {
                    //Matching type, but is it us or a base type?
                    IsCallToSuperclassMethod = !MiscUtils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(possibleTarget.DeclaringType!), InstanceBeingCalledOn.Type);
                }
            }
            
            //Resolve unmanaged => managed method.
            if (possibleTarget != null)
                ManagedMethodBeingCalled = possibleTarget.AsManaged();
            else
                AddComment($"Failed to resolve any matching method (there are {listOfCallableMethods.Count} at this address)");
            
            HandleReturnType(context);
        }
        
        private void HandleReturnType(MethodAnalysis<Arm64Instruction> context)
        {
            CreateLocalForReturnType(context);

            RegisterLocals(context);

            if (ManagedMethodBeingCalled?.FullName == "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle)")
            {
                if (Arguments?.Count > 1 && Arguments[1] is ConstantDefinition {Value: FieldDefinition fieldDefinition} && Arguments[0] is LocalDefinition {KnownInitialValue: AllocatedArray arr})
                {
                    instantiatedArrayValues = AnalysisUtils.ReadArrayInitializerForFieldDefinition(fieldDefinition, arr);
                    wasArrayInstantiation = true;
                    AddComment("Initializes array containing values: " + instantiatedArrayValues.ToStringEnumerable());
                }
            }
        }

        public override string? ToPsuedoCode()
        {
            if (wasArrayInstantiation)
            {
                var arrayType = ((ArrayType) ((LocalDefinition) Arguments![0]!).Type!).ElementType;
                return $"{Arguments![0]!.GetPseudocodeRepresentation()} = new {arrayType}[] {{{string.Join(", ", instantiatedArrayValues!)}}}";
            }
            
            return base.ToPsuedoCode();
        }
    }
}