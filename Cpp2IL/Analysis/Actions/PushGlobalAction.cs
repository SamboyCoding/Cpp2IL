using System;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions
{
    public class PushGlobalAction : BaseAction
    {
        private object _theGlobal;

        public PushGlobalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var offset = LibCpp2IlMain.ThePe.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            GlobalIdentifier realGlobal;
            if (LibCpp2IlMain.GetAnyGlobalByAddress(offset) is { } globalIdentifier && globalIdentifier.Offset == offset)
            {
                _theGlobal = globalIdentifier;
                realGlobal = globalIdentifier;
            }
            else
            {
                _theGlobal = new UnknownGlobalAddr(offset);
                return;
            }

            if (_theGlobal == null) return;

            if (realGlobal.Offset != offset) return;

            switch (realGlobal.IdentifierType)
            {
                case GlobalIdentifier.Type.TYPEREF:
                    var typeDefinition = Utils.TryResolveTypeReflectionData(realGlobal.ReferencedType!);
                    context.Stack.Push(context.MakeConstant(typeof(TypeDefinition), typeDefinition));
                    break;
                case GlobalIdentifier.Type.METHODREF:
                    var methodDefinition = SharedState.UnmanagedToManagedMethods[realGlobal.ReferencedMethod!];
                    context.Stack.Push(context.MakeConstant(typeof(MethodDefinition), methodDefinition));
                    break;
                case GlobalIdentifier.Type.FIELDREF:
                    var fieldDefinition = SharedState.UnmanagedToManagedFields[realGlobal.ReferencedField!];
                    context.Stack.Push(context.MakeConstant(typeof(FieldDefinition), fieldDefinition));
                    break;
                case GlobalIdentifier.Type.LITERAL:
                    context.Stack.Push(context.MakeConstant(typeof(string), realGlobal.Name));
                    break;
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Pushes {_theGlobal} onto the stack";
        }
    }
}