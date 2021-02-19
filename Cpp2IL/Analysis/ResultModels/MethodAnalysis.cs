using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.Actions;
using Cpp2IL.Analysis.Actions.Important;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Mono.Cecil.Cil.Instruction;

namespace Cpp2IL.Analysis.ResultModels
{
    public class MethodAnalysis
    {
        public readonly List<LocalDefinition> Locals = new List<LocalDefinition>();
        public readonly List<ConstantDefinition> Constants = new List<ConstantDefinition>();
        public List<BaseAction> Actions = new List<BaseAction>();
        public readonly List<ulong> IdentifiedJumpDestinationAddresses = new List<ulong>();
        public readonly List<ulong> ProbableLoopStarts = new List<ulong>();

        private ConstantDefinition EmptyRegConstant;

        internal ulong MethodStart;
        internal ulong AbsoluteMethodEnd;
        private readonly InstructionList _allInstructions;

        private MethodDefinition _method;
        
        private readonly List<IfData> IfOnlyBlockData = new List<IfData>();
        private readonly List<IfElseData> IfElseBlockData = new List<IfElseData>();
        private readonly List<LoopData> LoopBlockData = new List<LoopData>();

        public int IndentLevel;

        //This data is essentially our transient state - it must be stashed and unstashed when we jump into ifs etc.
        public List<LocalDefinition> FunctionArgumentLocals = new List<LocalDefinition>();
        private Dictionary<string, IAnalysedOperand> RegisterData = new Dictionary<string, IAnalysedOperand>();
        public Dictionary<int, LocalDefinition> StackStoredLocals = new Dictionary<int, LocalDefinition>();
        public Stack<IAnalysedOperand> Stack = new Stack<IAnalysedOperand>();
        public Stack<IAnalysedOperand> FloatingPointStack = new Stack<IAnalysedOperand>();

        internal MethodAnalysis(MethodDefinition method, ulong methodStart, ulong initialMethodEnd, InstructionList allInstructions)
        {
            _method = method;
            MethodStart = methodStart;
            AbsoluteMethodEnd = initialMethodEnd;
            _allInstructions = allInstructions;
            EmptyRegConstant = MakeConstant(typeof(int), 0, "0");

            var args = method.Parameters.ToList();
            //Set up parameters in registers & as locals.
            if (!LibCpp2IlMain.ThePe!.is32Bit)
            {
                var regList = new List<string> {"rcx", "rdx", "r8", "r9"};

                if (!method.IsStatic)
                    FunctionArgumentLocals.Add(MakeLocal(method.DeclaringType, "this", regList.RemoveAndReturn(0)).WithParameter(method.Body.ThisParameter));

                while (args.Count > 0 && regList.Count > 0)
                {
                    var arg = args.RemoveAndReturn(0);
                    var dest = regList.RemoveAndReturn(0);

                    FunctionArgumentLocals.Add(MakeLocal(arg.ParameterType.Resolve(), arg.Name, dest).WithParameter(arg));
                }
            }
            else if (!method.IsStatic)
            {
                //32-bit, instance method
                FunctionArgumentLocals.Add(MakeLocal(method.DeclaringType, "this"));
                Stack.Push(FunctionArgumentLocals.First());
            }

            //This is executed regardless of if we're x86 or x86-64. In x86-64, the first 4 args go in registers, and the rest go on the stack
            //in x86, everything is on the stack.
            while (args.Count > 0)
            {
                //Push remainder to stack
                var arg = args.RemoveAndReturn(0);
                var localDefinition = MakeLocal(arg.ParameterType.Resolve(), arg.Name);
                Stack.Push(localDefinition);
                FunctionArgumentLocals.Add(localDefinition);
            }
        }

        public bool IsVoid() => _method.ReturnType?.FullName == "System.Void";

        public bool IsConstructor() => _method.IsConstructor;

        public TypeDefinition GetTypeOfThis() => _method.DeclaringType;

        public bool IsStatic() => _method.IsStatic;


        public override string ToString()
        {
            return $"Method Analysis for {_method.FullName}";
        }

        public LocalDefinition MakeLocal(TypeReference type, string? name = null, string? reg = null, object? knownInitialValue = null)
        {
            var local = new LocalDefinition
            {
                Name = name ?? $"local{Locals.Count}",
                Type = type,
                KnownInitialValue = knownInitialValue
            };

            Locals.Add(local);

            if (reg != null)
                RegisterData[reg] = local;

            return local;
        }

        public ConstantDefinition MakeConstant(Type type, object value, string? name = null, string? reg = null)
        {
            var constant = new ConstantDefinition
            {
                Name = name ?? $"constant{Constants.Count}",
                Type = type,
                Value = value
            };

            Constants.Add(constant);

            if (reg != null)
                RegisterData[reg] = constant;

            return constant;
        }

        //These two are for mathematical operations on the stack.

        public void PushEmptyStackFrames(int count)
        {
            for (var i = 0; i < count; i++)
                Stack.Push(EmptyRegConstant);
        }

        public void PopStackFramesAndDiscard(int count)
        {
            for (var i = 0; i < count; i++)
                Stack.TryPop(out _);
        }

        public void SetRegContent(string reg, IAnalysedOperand? content)
        {
            RegisterData[reg] = content;
        }

        public void ZeroRegister(string reg)
        {
            SetRegContent(reg, EmptyRegConstant);
        }

        public IAnalysedOperand? GetOperandInRegister(string reg)
        {
            if (!RegisterData.TryGetValue(reg, out var result))
                return null;

            return result;
        }

        public LocalDefinition? GetLocalInReg(string reg)
        {
            if (!RegisterData.TryGetValue(reg, out var result))
                return null;

            if (!(result is LocalDefinition local)) return null;

            return local;
        }

        public ConstantDefinition? GetConstantInReg(string reg)
        {
            if (!RegisterData.TryGetValue(reg, out var result))
                return null;

            if (!(result is ConstantDefinition constant)) return null;

            return constant;
        }

        public bool IsEmptyRegArg(IAnalysedOperand analysedOperand)
        {
            return analysedOperand == EmptyRegConstant;
        }

        public bool IsJumpDestinationInThisFunction(ulong jumpTarget)
        {
            return jumpTarget >= MethodStart && jumpTarget <= AbsoluteMethodEnd;
        }

        public bool IsThereProbablyAnElseAt(ulong conditionalJumpTarget)
        {
            if (!IsJumpDestinationInThisFunction(conditionalJumpTarget)) return false;

            var instructionBeforeJump = _allInstructions.FirstOrDefault(i => i.NextIP == conditionalJumpTarget);

            if (instructionBeforeJump == default) return false;

            if (instructionBeforeJump.Mnemonic == Mnemonic.Jmp)
            {
                //Basically, if we're condition jumping to an instruction, and the instruction immediately prior to our jump destination is an
                //unconditional jump, then that first conditional jump is an if, and the unconditional jump is the end of that if statement, and this destination
                //is the start of an else.
                var unconditionalJumpTarget = instructionBeforeJump.NearBranchTarget;
                if (IsJumpDestinationInThisFunction(unconditionalJumpTarget) && unconditionalJumpTarget > conditionalJumpTarget)
                    return true;
            }

            return false;
        }

        private ulong GetEndOfElseBlock(ulong ipOfFirstInstructionInElse)
        {
            var endOfIf = _allInstructions.FirstOrDefault(i => i.NextIP == ipOfFirstInstructionInElse);

            if (endOfIf == default) return 0;

            if (endOfIf.Mnemonic == Mnemonic.Jmp)
            {
                //Basically, if we're condition jumping to an instruction, and the instruction immediately prior to our jump destination is an
                //unconditional jump, then that first conditional jump is an if, and the unconditional jump is the end of that if statement, and this destination
                //is the start of an else.
                var unconditionalJumpTarget = endOfIf.NearBranchTarget;
                if (IsJumpDestinationInThisFunction(unconditionalJumpTarget) && unconditionalJumpTarget > ipOfFirstInstructionInElse)
                    return unconditionalJumpTarget;
            }

            return 0;
        }

        private T SaveAnalysisState<T>(T state) where T : AnalysisState
        {
            state.FunctionArgumentLocals = FunctionArgumentLocals.Clone();
            state.StackStoredLocals = StackStoredLocals.Clone();
            state.RegisterData = RegisterData.Clone();
            state.FloatingPointStack = FloatingPointStack.Clone();
            return state;
        }

        private void LoadAnalysisState(AnalysisState state)
        {
            Stack = state.Stack;
            FunctionArgumentLocals = state.FunctionArgumentLocals;
            StackStoredLocals = state.StackStoredLocals;
            RegisterData = state.RegisterData;
            FloatingPointStack = state.FloatingPointStack;
        }

        public void RegisterIfElseStatement(ulong startOfIf, ulong startOfElse, BaseAction conditionalJump)
        {
            IfElseBlockData.Add(SaveAnalysisState(
                new IfElseData
                {
                    Stack = Stack.Clone(),
                    ConditionalJumpStatement = conditionalJump,
                    ElseStatementStart = startOfElse,
                    ElseStatementEnd = GetEndOfElseBlock(startOfElse),
                    IfStatementStart = startOfIf,
                }
            ));
        }
        
        public void RegisterIfStatement(ulong startOfIf, ulong endOfIf, BaseAction conditionalJump)
        {
            IfOnlyBlockData.Add(SaveAnalysisState(
                new IfData
                {
                    Stack = Stack.Clone(),
                    ConditionalJumpStatement = conditionalJump,
                    IfStatementEnd = endOfIf,
                    IfStatementStart = startOfIf,
                }
            ));
        }

        public ulong GetAddressOfIfBlockEndingHere(ulong end)
        {
            var ifData = IfOnlyBlockData.Find(i => i.IfStatementEnd == end);

            return ifData?.IfStatementStart ?? 0UL;
        }

        public ulong GetAddressOfElseThisIsTheEndOf(ulong jumpDest)
        {
            var ifElseData = IfElseBlockData.Find(i => i.ElseStatementEnd == jumpDest);

            return ifElseData?.ElseStatementStart ?? 0UL;
        }

        public ulong GetAddressOfAssociatedIfForThisElse(ulong elseStartAddr)
        {
            var ifElseData = IfElseBlockData.Find(i => i.ElseStatementStart == elseStartAddr);
            return ifElseData?.ConditionalJumpStatement.AssociatedInstruction.IP ?? 0UL;
        }

        public void PopStashedIfDataForElseAt(ulong elseStartAddr)
        {
            var ifElseData = IfElseBlockData.Find(i => i.ElseStatementStart == elseStartAddr);
            if (ifElseData == null)
                return;

            LoadAnalysisState(ifElseData);
        }

        public void PopStashedIfDataFrom(ulong ifStartAddr)
        {
            var ifData = IfOnlyBlockData.Find(i => i.IfStatementStart == ifStartAddr);
            
            if(ifData == null)
                return;
            
            LoadAnalysisState(ifData);
        }

        public ulong GetEndOfLoopWhichPossiblyStartsHere(ulong instructionIp)
        {
            //Look for a jump which is pointing at this instruction ip, and is after it.
            var matchingJump = _allInstructions.FirstOrDefault(i => i.IP > instructionIp && i.NearBranchTarget == instructionIp);

            return matchingJump.Mnemonic.IsJump() ? matchingJump.IP : 0UL;
        }

        public void RegisterLastInstructionOfLoopAt(ComparisonAction loopCondition, ulong lastStatementInLoop)
        {
            if (!IsJumpDestinationInThisFunction(lastStatementInLoop)) return;

            var matchingInstruction = _allInstructions.FirstOrDefault(i => i.IP == lastStatementInLoop);

            if (matchingInstruction.Mnemonic == Mnemonic.INVALID) return;

            var firstIpNotInLoop = matchingInstruction.NextIP;

            var firstInstructionNotInLoop = _allInstructions.FirstOrDefault(i => i.IP == firstIpNotInLoop);
            if (firstInstructionNotInLoop.Mnemonic == Mnemonic.INVALID) return;

            var data = new LoopData
            {
                ipFirstInstruction = loopCondition.AssociatedInstruction.IP,
                loopCondition = loopCondition,
                ipFirstInstructionNotInLoop = firstIpNotInLoop
            };
            SaveAnalysisState(data);
            
            LoopBlockData.Add(data);           
        }

        public bool IsIpInOneOrMoreLoops(ulong ip) => GetLoopConditionsInNestedOrder(ip).Length > 0;

        public ComparisonAction[] GetLoopConditionsInNestedOrder(ulong ip) =>
            LoopBlockData.Where(d => d.ipFirstInstruction <= ip && d.ipFirstInstructionNotInLoop > ip)
                .Select(d => d.loopCondition)
                .ToArray();

        public bool HaveWeExitedALoopOnThisInstruction(ulong ip)
        {
            var matchingBlock = LoopBlockData.FirstOrDefault(d => d.ipFirstInstructionNotInLoop == ip);

            return matchingBlock != null;
        }

        public void RestorePreLoopState(ulong ipFirstInstructionNotInLoop)
        {
            var matchingBlock = LoopBlockData.FirstOrDefault(d => d.ipFirstInstructionNotInLoop == ipFirstInstructionNotInLoop);

            if (matchingBlock == null) return;
            
            LoadAnalysisState(matchingBlock);
        }

        public Instruction GetILToLoad(LocalDefinition localDefinition, ILProcessor processor)
        {
            if (FunctionArgumentLocals.Contains(localDefinition))
            {
                return processor.Create(OpCodes.Ldarg, localDefinition.ParameterDefinition);
            }

            return processor.Create(OpCodes.Ldloc, localDefinition.Variable);
        }
    }
}