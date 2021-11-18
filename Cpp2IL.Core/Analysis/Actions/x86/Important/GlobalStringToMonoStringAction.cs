using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class GlobalStringToMonoStringAction : BaseAction<Instruction>
    {
        private string? _stringValue;
        private LocalDefinition? _localMade;

        public GlobalStringToMonoStringAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var stringConstant = LibCpp2IlMain.Binary!.is32Bit ? context.Stack.Peek() as ConstantDefinition : context.GetConstantInReg("rcx");

            if (LibCpp2IlMain.Binary!.is32Bit && stringConstant != null)
                context.Stack.Pop();

            var il2CppString = stringConstant?.Value as Il2CppString;

            _stringValue = il2CppString?.ContainedString;
            
            if(_stringValue == null)
                return;

            il2CppString!.HasBeenUsedAsAString = true;

            _localMade = context.MakeLocal(MiscUtils.StringReference, reg: "rax", knownInitialValue: _stringValue);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
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