using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Exceptions;
using Gee.External.Capstone.Arm64;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Mono.Cecil.Cil.Instruction;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class MethodAnalysis<TInstruction>
    {
        public delegate void PtrConsumer(ulong ptr);

        public readonly List<LocalDefinition> Locals = new();
        public readonly List<ConstantDefinition> Constants = new();
        public List<BaseAction<TInstruction>> Actions = new();
        public readonly List<ulong> IdentifiedJumpDestinationAddresses = new();
        public readonly List<ulong> ProbableLoopStarts = new();
        public readonly Dictionary<BaseAction<TInstruction>, List<Instruction>> JumpTargetsToFixByAction = new();

        public event PtrConsumer OnExpansionRequested = _ => { };

        internal ConstantDefinition EmptyRegConstant;
        private List<string> _parameterDestRegList = new();
        private int _numParamsAdded;

        internal ulong MethodStart;
        internal ulong AbsoluteMethodEnd;
        private readonly List<TInstruction> _allInstructions;

        private MethodDefinition? _method;

        private readonly List<IfData<TInstruction>> IfOnlyBlockData = new();
        private readonly List<IfElseData<TInstruction>> IfElseBlockData = new();
        private readonly List<LoopData<TInstruction>> LoopBlockData = new();

        public readonly Dictionary<ulong, ulong> GotoDestinationsToStartDict = new();

        public int IndentLevel;

        //This data is essentially our transient state - it must be stashed and unstashed when we jump into ifs etc.
        public List<LocalDefinition> FunctionArgumentLocals = new();
        internal Dictionary<string, IAnalysedOperand> RegisterData = new();
        public Dictionary<int, LocalDefinition> StackStoredLocals = new();
        public Stack<IAnalysedOperand> Stack = new();
        public Stack<IAnalysedOperand> FloatingPointStack = new();

        public TypeDefinition DeclaringType => _method?.DeclaringType ?? Utils.ObjectReference;
        public TypeReference ReturnType => _method?.ReturnType ?? Utils.TryLookupTypeDefKnownNotGeneric("System.Void")!;

        public Arm64ReturnValueLocation Arm64ReturnValueLocation;

        public static readonly List<Type> ActionsWhichGenerateNoIL = new()
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
        internal MethodAnalysis(ulong methodStart, ulong initialMethodEnd, IList<TInstruction> allInstructions)
        {
            MethodStart = methodStart;
            AbsoluteMethodEnd = initialMethodEnd;
            _allInstructions = new(allInstructions);
            EmptyRegConstant = MakeConstant(typeof(int), 0, "0");

            //Can't handle params - we don't have any - but can populate reg list if needed.
            if (LibCpp2IlMain.Binary!.InstructionSet == InstructionSet.X86_64)
                _parameterDestRegList = new() { "rcx", "rdx", "r8", "r9" };
        }

        internal MethodAnalysis(MethodDefinition method, ulong methodStart, ulong initialMethodEnd, IList<TInstruction> allInstructions)
        {
            _method = method;
            MethodStart = methodStart;
            AbsoluteMethodEnd = initialMethodEnd;
            _allInstructions = new(allInstructions);
            EmptyRegConstant = MakeConstant(typeof(int), 0, "0");

            var args = method.Parameters.ToList();
            var haveHandledMethodInfoArg = false;
            //Set up parameters in registers & as locals.
            //Arm64 is quite complicated here.
            if (LibCpp2IlMain.Binary!.InstructionSet == InstructionSet.ARM64)
            {
                HandleArm64Parameters();
            }
            else if (LibCpp2IlMain.Binary.InstructionSet != InstructionSet.X86_32)
            {
                _parameterDestRegList = LibCpp2IlMain.Binary.InstructionSet switch
                {
                    InstructionSet.X86_64 => new() { "rcx", "rdx", "r8", "r9" },
                    InstructionSet.ARM32 => new() {"r0", "r1", "r2", "r3"},
                    _ => throw new UnsupportedInstructionSetException(),
                };

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

        private void HandleArm64Parameters()
        {
            //Parameters are pushed to x0-x7 but also v0-v7 if vectors.
            //But the interesting part is that having something in x0 doesn't mean you can't have something in v0.
            //So you can have, essentially, 14 parameters in registers before you go to the stack.
            //However, the return value of the function also has to go into one of x0-x7 or v0-v7. So in practise that limit is 
            //Less than 14 for non-void functions. Also, arm functions can return more than one value in this way.
            //Specifically: 
            //Integer values go in x0.
            //Floating point values go in v0.
            //Simple combinations of floating values (e.g. vectors) which have no constructor, private fields, base class, etc, and <= 4 fields, go in v0-v3 as required.
            //Simple structs that: Have no con or destructor, no private or protected fields, and no base class, and are:
            //    8 bytes => x0
            //    8-16 bytes => x0-x1
            //    >16 bytes => calling function allocates memory and passes pointer in x8. Called function does what it needs with that.
            //But ^ this only applies for static or global methods (those which don't have a "this" parameter, which has to go in x0)
            //For all other types and for functions with a "this" param:
            //Calling function allocates memory and passes in as x0, or x1 if x0 is taken by "this". Called function does what it needs.
            var xCount = 0;
            var vCount = 0;
            
            //First things first: this parameter, if it exists.
            if (!_method!.IsStatic)
            {
                _method.Body.ThisParameter.Name = "this";
                var local = MakeLocal(_method.DeclaringType, "this", $"x{xCount}").WithParameter(_method.Body.ThisParameter);
                FunctionArgumentLocals.Add(local);
                xCount++;
            }
            
            //Now: return type
            if (!IsVoid())
            {
                //What type of object do we have?
                if (_method.ReturnType.ShouldBeInFloatingPointRegister())
                {
                    //Simple floating point => v0
                    Arm64ReturnValueLocation = Arm64ReturnValueLocation.V0;
                    vCount++;
                } else if (_method.ReturnType.IsPrimitive && _method.ReturnType.Name != "String" && xCount == 0)
                {
                    //Simple scalar => x0 if we're static
                    Arm64ReturnValueLocation = Arm64ReturnValueLocation.X0;
                }
                //TODO Investigate exactly where UnityEngine.Vector3 ends up - is it simple enough to go in v0-v3? Or because it has constructors etc, does it get treated as complex?
                //TODO Equally, are there any plain data objects which take the X0_X1 slot? Or in the same vein, the x8 pointer slot?
                else if (xCount == 0)
                {
                    //No this, complex object => pointer in x0
                    Arm64ReturnValueLocation = Arm64ReturnValueLocation.POINTER_X0;
                    xCount++;
                }
                else
                {
                    //Have a this, and a complex object => pointer in x1
                    Arm64ReturnValueLocation = Arm64ReturnValueLocation.POINTER_X1;
                }
            }
            
            //Finally: params themselves
            foreach (var methodParameter in _method.Parameters)
            {
                var useV = methodParameter.ParameterType.ShouldBeInFloatingPointRegister();

                var reg = useV ? "v" : "x";
                var thisIdx = useV ? vCount++ : xCount++;
                reg += thisIdx;
                
                if(thisIdx >= 8)
                    continue; //TODO Stack Params.

                var name = methodParameter.Name;
                if (string.IsNullOrWhiteSpace(name))
                    name = methodParameter.Name = $"cpp2il__autoParamName__idx_{vCount + xCount - 1}";
                
                var local = MakeLocal(methodParameter.ParameterType, name, reg).WithParameter(methodParameter);
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
                    name = arg.Name = $"cpp2il__autoParamName__idx_{_numParamsAdded}";

                FunctionArgumentLocals.Add(MakeLocal(arg.ParameterType, name, dest).WithParameter(arg));
            }
            else
            {
                //Push any remainders to stack
                var localDefinition = MakeLocal(arg.ParameterType.Resolve(), arg.Name).WithParameter(arg);
                Stack.Push(localDefinition);
                FunctionArgumentLocals.Add(localDefinition);
            }

            _numParamsAdded++;
        }

        public void ExpandAnalysisToIncludeBlockStartingAt(ulong ptr)
        {
            if (_allInstructions.All(i => i.GetInstructionAddress() < ptr))
                OnExpansionRequested(ptr);
        }

        internal void AddInstructions(IList<TInstruction> newInstructions, ulong newEnd)
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
            if (typeof(TInstruction) == typeof(Iced.Intel.Instruction))
                return IsThereProbablyAnElseAtX86Version(conditionalJumpTarget);
            if (typeof(TInstruction) == typeof(Arm64Instruction))
                return false; //TODO

            throw new($"Not Implemented: {typeof(TInstruction)}");
        }

        private bool IsThereProbablyAnElseAtX86Version(ulong conditionalJumpTarget)
        {
            if (!IsJumpDestinationInThisFunction(conditionalJumpTarget)) return false;

            var instructionBeforeJump = (Iced.Intel.Instruction)(object)_allInstructions.FirstOrDefault(i => i.GetNextInstructionAddress() == conditionalJumpTarget)!;

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
        
        public ulong GetEndOfElseBlock(ulong ipOfFirstInstructionInElse)
        {
            if (typeof(TInstruction) == typeof(Iced.Intel.Instruction))
                return GetEndOfElseBlockX86Version(ipOfFirstInstructionInElse);
            if (typeof(TInstruction) == typeof(Arm64Instruction))
                return 0; //TODO

            throw new($"Not Implemented: {typeof(TInstruction)}");
        }

        private ulong GetEndOfElseBlockX86Version(ulong ipOfFirstInstructionInElse)
        {
            var endOfIf = (Iced.Intel.Instruction)(object)_allInstructions.FirstOrDefault(i => i.GetNextInstructionAddress() == ipOfFirstInstructionInElse)!;

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

        public void RegisterIfElseStatement(ulong startOfIf, ulong startOfElse, BaseAction<TInstruction> conditionalJump)
        {
            IfElseBlockData.Add(SaveAnalysisState(
                new IfElseData<TInstruction>
                {
                    Stack = Stack.Clone(),
                    ConditionalJumpStatement = conditionalJump,
                    ElseStatementStart = startOfElse,
                    ElseStatementEnd = GetEndOfElseBlock(startOfElse),
                    IfStatementStart = startOfIf,
                }
            ));
        }

        public void RegisterIfStatement(ulong startOfIf, ulong endOfIf, BaseAction<TInstruction> conditionalJump)
        {
            IfOnlyBlockData.Add(SaveAnalysisState(
                new IfData<TInstruction>
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
            if (ifElseData == null)
                return 0UL;

            return Utils.GetAddressOfInstruction(ifElseData.ConditionalJumpStatement.AssociatedInstruction);
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

            if (ifData == null)
                return;

            LoadAnalysisState(ifData);
        }

        public ulong GetEndOfLoopWhichPossiblyStartsHere(ulong instructionIp)
        {
            if (typeof(TInstruction) == typeof(Iced.Intel.Instruction))
                return GetEndOfLoopWhichPossiblyStartsHereX86Version(instructionIp);
            if (typeof(TInstruction) == typeof(Arm64Instruction))
                return 0; //TODO

            throw new($"Not Implemented: {typeof(TInstruction)}");
        }

        private ulong GetEndOfLoopWhichPossiblyStartsHereX86Version(ulong instructionIp)
        {
            //Look for a jump which is pointing at this instruction ip, and is after it.

            Iced.Intel.Instruction matchingJump = default;
            //This is a for-loop specifically because it's quite commonly called, and making it a foreach makes it take four times longer.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var index = 0; index < _allInstructions.Count; index++)
            {
                var instruction = (Iced.Intel.Instruction)(object)_allInstructions[index]!;

                if (instruction.IP <= instructionIp)
                    continue;
                if (!instruction.Mnemonic.IsJump())
                    continue;

                if (instruction.NearBranchTarget == instructionIp)
                {
                    matchingJump = instruction;
                    break;
                }
            }

            // var matchingJump = _allInstructions.Cast<Iced.Intel.Instruction>()
            //     .Where(i => i.Mnemonic.IsJump())
            //     .FirstOrDefault(i => i.GetInstructionAddress() > instructionIp && i.NearBranchTarget == instructionIp);

            return matchingJump.Mnemonic.IsJump() ? matchingJump.GetInstructionAddress() : 0UL;
        }

        public void RegisterLastInstructionOfLoopAt(AbstractComparisonAction<TInstruction> loopCondition, ulong lastStatementInLoop)
        {
            if (!IsJumpDestinationInThisFunction(lastStatementInLoop)) return;

            var matchingInstruction = _allInstructions.FirstOrDefault(i => i.GetInstructionAddress() == lastStatementInLoop);

            if (matchingInstruction == null) return;

            var firstIpNotInLoop = matchingInstruction.GetNextInstructionAddress();

            var firstInstructionNotInLoop = _allInstructions.FirstOrDefault(i => i.GetInstructionAddress() == firstIpNotInLoop);
            if (firstInstructionNotInLoop == null) return;

            var data = new LoopData<TInstruction>
            {
                ipFirstInstruction = Utils.GetAddressOfInstruction(loopCondition.AssociatedInstruction),
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

        public AbstractComparisonAction<TInstruction>[] GetLoopConditionsInNestedOrder(ulong ip) =>
            LoopBlockData.Where(d => d.ipFirstInstruction <= ip && d.ipFirstInstructionNotInLoop > ip)
                .Select(d => d.loopCondition)
                .ToArray();

        public bool HaveWeExitedALoopOnThisInstruction(ulong ip)
        {
            var matchingBlock = LoopBlockData.FirstOrDefault(d => d.ipFirstInstructionNotInLoop == ip);

            return matchingBlock != null;
        }

        public AbstractComparisonAction<TInstruction>? GetLoopWhichJustEnded(ulong ip)
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

        public Instruction GetIlToLoad(LocalDefinition localDefinition, ILProcessor processor)
        {
            if (localDefinition.ParameterDefinition != null)
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
                .FirstOrDefault(a => Utils.GetAddressOfInstruction(a.AssociatedInstruction) >= jumpTarget);

            if (target == null)
                return;

            if (!JumpTargetsToFixByAction.ContainsKey(target))
                JumpTargetsToFixByAction[target] = new();

            JumpTargetsToFixByAction[target].Add(jumpInstruction);
        }
    }
}