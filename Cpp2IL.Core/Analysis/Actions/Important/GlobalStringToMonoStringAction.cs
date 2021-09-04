using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class GlobalStringToMonoStringAction : BaseAction<Instruction>
    {
        private string? _stringValue;
        private LocalDefinition? _localMade;

        public GlobalStringToMonoStringAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var stringConstant = LibCpp2IlMain.Binary!.is32Bit ? context.Stack.Peek() as ConstantDefinition : context.GetConstantInReg("rcx");

            if (LibCpp2IlMain.Binary!.is32Bit && stringConstant != null)
                context.Stack.Pop();

            _stringValue = (stringConstant?.Value as Il2CppString)?.ContainedString;
            
            if(_stringValue == null)
                return;

            _localMade = context.MakeLocal(Utils.StringReference, reg: "rax", knownInitialValue: _stringValue);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"System.String {_localMade?.GetPseudocodeRepresentation()} = \"{_stringValue}\"";
        }

        public override string ToTextSummary()
        {
            if (_localMade == null)
                return "[!!] Calls il2cpp_string_new with unknown string literal!";
            
            return $"[!] Creates a new System.String with the value \"{_stringValue}\" and stores it in new local {_localMade?.Name}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}