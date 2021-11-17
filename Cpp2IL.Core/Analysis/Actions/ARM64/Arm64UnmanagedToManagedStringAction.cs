using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64UnmanagedToManagedStringAction : BaseAction<Arm64Instruction>
    {
        private string? _stringValue;
        private LocalDefinition? _localMade;
        
        public Arm64UnmanagedToManagedStringAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var stringConstant = context.GetConstantInReg("x0");

            if (LibCpp2IlMain.Binary!.is32Bit && stringConstant != null)
                context.Stack.Pop();

            _stringValue = (stringConstant?.Value as Il2CppString)?.ContainedString;
            
            if(_stringValue == null)
                return;

            _localMade = context.MakeLocal(MiscUtils.StringReference, reg: "x0", knownInitialValue: _stringValue);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
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