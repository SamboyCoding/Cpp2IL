using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions;
using Cpp2IL.Core.Analysis.Actions.Important;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Mono.Cecil.Cil.Instruction;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class MethodAnalysis
    {
        public delegate void PtrConsumer(ulong ptr);
        
        public KeyFunctionAddresses KeyFunctionAddresses { get; }
        
        public readonly List<LocalDefinition> Locals = new List<LocalDefinition>();
        public readonly List<ConstantDefinition> Constants = new List<ConstantDefinition>();
        public List<BaseAction> Actions = new List<BaseAction>();
        public readonly List<ulong> IdentifiedJumpDestinationAddresses = new List<ulong>();
        public readonly List<ulong> ProbableLoopStarts = new List<ulong>();
        public readonly Dictionary<BaseAction, List<Instruction>> JumpTargetsToFixByAction = new Dictionary<BaseAction, List<Instruction>>();

        public event PtrConsumer OnExpansionRequested;

        private ConstantDefinition EmptyRegConstant;
        private List<string> _parameterDestRegList = new List<string>();
        private int numParamsAdded = 0;

        internal ulong MethodStart;
        internal ulong AbsoluteMethodEnd;
        private readonly InstructionList _allInstructions;

        private MethodDefinition? _method;
        
        private readonly List<IfData> IfOnlyBlockData = new List<IfData>();
        private readonly List<IfElseData> IfElseBlockData = new List<IfElseData>();
        private readonly List<LoopData> LoopBlockData = new List<LoopData>();

        public readonly Dictionary<ulong, ulong> GotoDestinationsToStartDict = new Dictionary<ulong, ulong>();

        public int IndentLevel;

        //This data is essentially our transient state - it must be stashed and unstashed when we jump into ifs etc.
        public List<LocalDefinition> FunctionArgumentLocals = new List<LocalDefinition>();
        private Dictionary<string, IAnalysedOperand> RegisterData = new Dictionary<string, IAnalysedOperand>();
        public Dictionary<int, LocalDefinition> StackStoredLocals = new Dictionary<int, LocalDefinition>();
        public Stack<IAnalysedOperand> Stack = new Stack<IAnalysedOperand>();
        public Stack<IAnalysedOperand> FloatingPointStack = new Stack<IAnalysedOperand>();

        public TypeDefinition DeclaringType => _method?.DeclaringType ?? Utils.ObjectReference;
        public TypeReference ReturnType => _method?.ReturnType ?? Utils.TryLookupTypeDefKnownNotGeneric("System.Void")!;

        public static readonly List<Type> ActionsWhichGenerateNoIL = new List<Type>
        {
            typeof(GoToMarkerAction),
            typeof(GoToDestinationMarker),
            typeof(EndIfMarkerAction),
            typeof(ElseMarkerAction),
            typeof(EndWhileMarkerAction),
            typeof(ConstantToRegAction),
            typeof(LoadConstantUsingLeaAction)
        };

        //For analysing cpp-only methods, like attribute generators
        internal MethodAnalysis(ulong methodStart, ulong initialMethodEnd, InstructionList allInstructions, KeyFunctionAddresses keyFunctionAddresses)
        {
            MethodStart = methodStart;
            AbsoluteMethodEnd = initialMethodEnd;
            _allInstructions = allInstructions;
            KeyFunctionAddresses = keyFunctionAddresses;
            EmptyRegConstant = MakeConstant(typeof(int), 0, "0");
            
            //Can't handle params - we don't have any - but can populate reg list if needed.
            if (!LibCpp2IlMain.Binary!.is32Bit) 
                _parameterDestRegList = new List<string> {"rcx", "rdx", "r8", "r9"};
        }

        internal MethodAnalysis(MethodDefinition method, ulong methodStart, ulong initialMethodEnd, InstructionList allInstructions, KeyFunctionAddresses keyFunctionAddresses)
        {
            _method = method;
            MethodStart = methodStart;
            AbsoluteMethodEnd = initialMethodEnd;
            _allInstructions = allInstructions;
            KeyFunctionAddresses = keyFunctionAddresses;
            EmptyRegConstant = MakeConstant(typeof(int), 0, "0");

            var args = method.Parameters.ToList();
            var haveHandledMethodInfoArg = false;
            //Set up parameters in registers & as locals.
            if (!LibCpp2IlMain.Binary!.is32Bit)
            {
                _parameterDestRegList = new List<string> {"rcx", "rdx", "r8", "r9"};

                if (!method.IsStatic)
                {
                    method.Body.ThisParameter.Name = "this";
                    FunctionArgumentLocals.Add(MakeLocal(method.DeclaringType, "this", _parameterDestRegList.RemoveAndReturn(0)).WithParameter(method.Body.ThisParameter));
                }

                while (args.Count > 0)
                {
                    var arg = args.RemoveAndReturn(0);
                    AddParameter(arg);
                }

                if (_parameterDestRegList.Count > 0)
                {
                    FunctionArgumentLocals.Add(MakeLocal(Utils.TryLookupTypeDefKnownNotGeneric("System.Reflection.MethodInfo")!, "il2cppMethodInfo", _parameterDestRegList.RemoveAndReturn(0)).MarkAsIl2CppMethodInfo());
                    haveHandledMethodInfoArg = true;
                }
            }
            else if (!method.IsStatic)
            {
                //32-bit, instance method
                method.Body.ThisParameter.Name = "this";
                FunctionArgumentLocals.Add(MakeLocal(method.DeclaringType, "this").WithParameter(method.Body.ThisParameter));
                Stack.Push(FunctionArgumentLocals.First());
            }

            //This is executed regardless of if we're x86 or x86-64. In x86-64, the first 4 args go in registers, and the rest go on the stack
            //in x86, everything is on the stack.
            while (args.Count > 0)
            {
                //Push remainder to stack
                var arg = args.RemoveAndReturn(0);
                AddParameter(arg);
            }

            if (!haveHandledMethodInfoArg)
            {
                var local = MakeLocal(Utils.TryLookupTypeDefKnownNotGeneric("System.Reflection.MethodInfo")!, "il2cppMethodInfo").MarkAsIl2CppMethodInfo();
                Stack.Push(local);
                FunctionArgumentLocals.Add(local);
            }
        }

        internal void AddParameter(ParameterDefinition arg)
        {
            if (_parameterDestRegList.Count > 0)
            {
                var dest = _parameterDestRegList.RemoveAndReturn(0);

                if (arg.ParameterType.ShouldBeInFloatingPointRegister())
                    dest = Utils.GetFloatingRegister(dest);

                var name = arg.Name;
                if (string.IsNullOrWhiteSpace(name))
                    name = arg.Name = $"cpp2il__autoParamName__idx_{numParamsAdded}";
                
                FunctionArgumentLocals.Add(MakeLocal(arg.ParameterType, name, dest).WithParameter(arg));
            }
            else
            {
                //Push any remainders to stack
                var localDefinition = MakeLocal(arg.ParameterType.Resolve(), arg.Name).WithParameter(arg);
                Stack.Push(localDefinition);
                FunctionArgumentLocals.Add(localDefinition);
            }
            
            numParamsAdded++;
        }

        public void ExpandAnalysisToIncludeBlockStartingAt(ulong ptr)
        {
            if(_allInstructions.All(i => i.IP < ptr))
                OnExpansionRequested(ptr);
        }

        internal void AddInstructions(InstructionList newInstructions, ulong newEnd)
        {
            _allInstructions.AddRange(newInstructions);
            AbsoluteMethodEnd = newEnd;
        }

        public bool IsVoid() => _method?.ReturnType?.FullName == "System.Void";

        public bool IsConstructor() => _method?.IsConstructor ?? false;

        public TypeDefinition GetTypeOfThis() => _method?.DeclaringType ?? Utils.ObjectReference;

        public bool IsStatic() => _method?.IsStatic ?? true;


        public override string ToString()
        {
            return $"Method Analysis for {_method?.FullName ?? "A cpp method"}";
        }

        public LocalDefinition MakeLocal(TypeReference type, string? name = null, string? reg = null, object? knownInitialValue = null)
        {
            // if (type == null)
            //     throw new Exception($"Tried to create a local (in reg {reg}, with name {name}), with a null type");
            
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

        public void SetRegContent(string reg, IAnalysedOperand content)
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
            return jumpTarget >= MethodStart && jumpTarget < AbsoluteMethodEnd;
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

        public void RegisterGotoDestination(ulong source, ulong dest)
        {
            GotoDestinationsToStartDict[dest] = source;
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
        
        public ComparisonAction? GetLoopWhichJustEnded(ulong ip)
        {
            var matchingBlock = LoopBlockData.FirstOrDefault(d => d.ipFirstInstructionNotInLoop == ip);

            return matchingBlock?.loopCondition;
        }

        public void RestorePreLoopState(ulong ipFirstInstructionNotInLoop)
        {
            var matchingBlock = LoopBlockData.FirstOrDefault(d => d.ipFirstInstructionNotInLoop == ipFirstInstructionNotInLoop);

            if (matchingBlock == null) return;
            
            LoadAnalysisState(matchingBlock);
        }

        public (ulong startAddr, ulong endAddr) GetMostRecentBlock(ulong currAddr)
        {
            var starts =
                IfOnlyBlockData.Select(i => i.IfStatementStart)
                    .Concat(IfElseBlockData.Select(i => i.ElseStatementStart < currAddr ? i.ElseStatementStart : i.IfStatementStart))
                    .Concat(LoopBlockData.Select(l => l.ipFirstInstruction))
                    .ToList();

            if (!starts.Any())
                return (0, 0);
            
            var latestStart = starts.Max();

            if (IfOnlyBlockData.FirstOrDefault(i => i.IfStatementStart == latestStart) is { } data1)
                return (startAddr: data1.IfStatementStart, endAddr: data1.IfStatementEnd);

            if (IfElseBlockData.FirstOrDefault(i => i.IfStatementStart == latestStart) is { } data2)
                return (startAddr: data2.IfStatementStart, endAddr: data2.ElseStatementStart);
            
            if (IfElseBlockData.FirstOrDefault(i => i.ElseStatementStart == latestStart) is { } data3)
                return (startAddr: data3.ElseStatementStart, endAddr: data3.ElseStatementEnd);
            
            if (LoopBlockData.FirstOrDefault(i => i.ipFirstInstruction == latestStart) is { } data4)
                return (startAddr: data4.ipFirstInstruction, endAddr: data4.ipFirstInstructionNotInLoop);

            return (0, 0);
        }

        public Instruction GetILToLoad(LocalDefinition localDefinition, ILProcessor processor)
        {
            if(localDefinition.ParameterDefinition != null)
                return processor.Create(OpCodes.Ldarg, localDefinition.ParameterDefinition);
            
            if (FunctionArgumentLocals.Contains(localDefinition))
            {
                if (localDefinition.ParameterDefinition == null)
                    throw new TaintedInstructionException($"Local {localDefinition.Name} is a function parameter but is missing its parameter definition");
            }

            if (localDefinition.Variable == null)
                throw new TaintedInstructionException($"Local {localDefinition.Name} is a variable but it has been stripped out. Are you missing a call to RegisterUsedLocal?");

            return processor.Create(OpCodes.Ldloc, localDefinition.Variable);
        }

        public void RegisterInstructionTargetToSwapOut(Instruction jumpInstruction, ulong jumpTarget)
        {
            var target = Actions
                .Where(a => !ActionsWhichGenerateNoIL.Contains(a.GetType()))
                .FirstOrDefault(a => a.AssociatedInstruction.IP >= jumpTarget);
            
            if(target == null)
                return;

            if (!JumpTargetsToFixByAction.ContainsKey(target))
                JumpTargetsToFixByAction[target] = new List<Instruction>();
            
            JumpTargetsToFixByAction[target].Add(jumpInstruction);
        }
    }
}