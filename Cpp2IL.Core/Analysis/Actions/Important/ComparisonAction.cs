using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class ComparisonAction : BaseAction
    {
        public readonly IComparisonArgument? ArgumentOne;
        public readonly IComparisonArgument? ArgumentTwo;

        private readonly string? ArgumentOneRegister;
        private readonly string? ArgumentTwoRegister;

        public readonly bool unimportantComparison;

        public readonly ulong EndOfLoopAddr;

        public ComparisonAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var r0 = Utils.GetRegisterNameNew(instruction.Op0Register);
            var r1 = Utils.GetRegisterNameNew(instruction.Op1Register);

            var unimportant1 = false;
            var unimportant2 = false;
            if (r0 != "rsp") 
                ArgumentOne = ExtractArgument(context, instruction, r0, 0, instruction.Op0Kind, out unimportant1, out ArgumentOneRegister);

            if (r1 != "rsp")
                ArgumentTwo = ExtractArgument(context, instruction, r1, 1, instruction.Op1Kind, out unimportant2, out ArgumentTwoRegister);

            if (ArgumentOne is ConstantDefinition {Value: UnknownGlobalAddr globalAddr} cons && ArgumentTwo is LocalDefinition {Type: { }, KnownInitialValue: null} loc2)
            {
                try
                {
                    Utils.CoerceUnknownGlobalValue(loc2.Type, globalAddr, cons, false);
                    unimportant1 = false;
                }
                catch
                {
                    // ignored
                }
            }

            if (ArgumentTwo is ConstantDefinition {Value: UnknownGlobalAddr globalAddr2} cons2 && ArgumentOne is LocalDefinition {Type: { }, KnownInitialValue: null} loc1)
            {
                try
                {
                    Utils.CoerceUnknownGlobalValue(loc1.Type, globalAddr2, cons2, false);
                    unimportant2 = false;
                }
                catch
                {
                    // ignored
                }
            }

            unimportantComparison = unimportant1 || unimportant2;

            if (context.GetEndOfLoopWhichPossiblyStartsHere(instruction.IP) is {} endOfLoop && endOfLoop != 0)
            {
                EndOfLoopAddr = endOfLoop;
                context.RegisterLastInstructionOfLoopAt(this, endOfLoop);
            }

            if (ArgumentOne is LocalDefinition l1)
                RegisterUsedLocal(l1);
            
            if (ArgumentTwo is LocalDefinition l2)
                RegisterUsedLocal(l2);
        }

        public bool IsEitherArgument(IComparisonArgument c) => c == ArgumentOne || c == ArgumentTwo;
        
        public IComparisonArgument? GetArgumentAssociatedWithRegister(string regName)
        {
            return ArgumentOneRegister == regName ? ArgumentOne : ArgumentTwoRegister == regName ? ArgumentTwo : null;
        }

        internal bool IsProbablyWhileLoop() => EndOfLoopAddr != 0;

        private static IComparisonArgument? ExtractArgument(MethodAnalysis context, Instruction instruction, string registerName, int operandIdx, OpKind opKind, out bool unimportant, out string? argumentRegister)
        {
            var globalMemoryOffset = LibCpp2IlMain.Binary!.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            
            unimportant = false;
            argumentRegister = null;

            if (opKind == OpKind.Register)
            {
                argumentRegister = registerName;
                var op = context.GetOperandInRegister(registerName);

                if (op is ConstantDefinition {Value: MethodDefinition _} || op is ConstantDefinition { Value: UnknownGlobalAddr _} || op is ConstantDefinition {Value: Il2CppString _})
                    //Ignore comparisons with looked-up method defs or unknown globals.
                    unimportant = true;

                return op;
            }

            if (opKind.IsImmediate())
                return context.MakeConstant(typeof(ulong), instruction.GetImmediate(operandIdx));
            
            if (opKind == OpKind.Memory && instruction.MemoryBase != Register.None && instruction.MemoryBase != Register.RIP)
            {
                var name = Utils.GetRegisterNameNew(instruction.MemoryBase);
                if (context.GetLocalInReg(name) is { } local)
                {
                    if (local.Type?.IsArray == false)
                    {
                        if (local.Type?.Resolve() == null) return null;
                        
                        var fields = SharedState.FieldsByType[local.Type.Resolve()];
                        var fieldName = fields.FirstOrDefault(f => f.Offset == instruction.MemoryDisplacement32).Name;
                        
                        if (string.IsNullOrEmpty(fieldName)) return null;

                        var field = local.Type.Resolve().Fields.FirstOrDefault(f => f.Name == fieldName);

                        if (field == null) return null;

                        argumentRegister = name;
                        return new ComparisonDirectFieldAccess
                        {
                            fieldAccessed = field,
                            localAccessedOn = local
                        };
                    }

                    if (Il2CppArrayUtils.IsIl2cppLengthAccessor(instruction.MemoryDisplacement32))
                    {
                        argumentRegister = name;
                        return new ComparisonDirectPropertyAccess
                        {
                            localAccessedOn = local,
                            propertyAccessed = Il2CppArrayUtils.GetLengthProperty()
                        };
                    }

                    var nameOfField = Il2CppArrayUtils.GetOffsetName(instruction.MemoryDisplacement32);
                    argumentRegister = name;
                    return context.MakeConstant(typeof(string), $"{{il2cpp array field {local.Name}->{nameOfField}}}");
                }

                if (!(context.GetConstantInReg(name) is { } constant)) 
                    return null; //Unknown operand - memory type, not a global, but not a constant, or local
                
                var defaultLabel = $"{{il2cpp field on {constant}, offset 0x{instruction.MemoryDisplacement32:X}}}";
                if (constant.Type == typeof(TypeDefinition) || constant.Type == typeof(TypeReference))
                {
                    unimportant = true;
                    var offset = instruction.MemoryDisplacement32;
                    var label = Il2CppClassUsefulOffsets.GetOffsetName(offset);

                    label = label == null ? defaultLabel : $"{{il2cpp field {constant.Value}->{label}}}";
                    argumentRegister = name;
                    return context.MakeConstant(typeof(string), label);
                }

                unimportant = true;
                return context.MakeConstant(typeof(string), defaultLabel);
            }

            if (LibCpp2IlMain.GetAnyGlobalByAddress(globalMemoryOffset) is {} usage)
                return context.MakeConstant(typeof(MetadataUsage), usage);
            
            unimportant = true;
            return context.MakeConstant(typeof(UnknownGlobalAddr), new UnknownGlobalAddr(globalMemoryOffset));
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            var display1 = ArgumentOne?.ToString();
            var display2 = ArgumentTwo?.ToString();

            //Only show the important [!] if this is an important comparison (i.e. not an il2cpp one)
            return unimportantComparison ? $"Compares {display1} and {display2}" : $"[!] Compares {display1} and {display2}";
        }
    }
}