using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis
{
    public abstract class AsmAnalyzerBase<T> : IAsmAnalyzer
    {
        public IList<T> Instructions { get; }
        protected MethodDefinition? MethodDefinition;
        protected ulong MethodEnd;
        protected Il2CppBinary CppAssembly;
        internal List<TypeDefinition> AttributesForRestoration;
        protected bool IsGenuineMethod;
        internal MethodAnalysis<T> Analysis;
        private readonly StringBuilder _methodFunctionality = new();
        protected readonly List<T> _instructions;
        protected BaseKeyFunctionAddresses _keyFunctionAddresses;

        internal AsmAnalyzerBase(ulong methodPointer, IEnumerable<T> instructions, BaseKeyFunctionAddresses keyFunctionAddresses)
        {
            _keyFunctionAddresses = keyFunctionAddresses ?? throw new ArgumentNullException(nameof(keyFunctionAddresses));
            _instructions = new();
            CppAssembly = LibCpp2IlMain.Binary!;
            
            foreach (var instruction in instructions)
            {
                _instructions.Add(instruction);
            }
            
            Analysis = new(methodPointer, MethodEnd, _instructions);
            Analysis.OnExpansionRequested += AnalysisRequestedExpansion;

            if (FindInstructionWhichOverran(out var idx))
            {
                _instructions = new(_instructions.Take(idx).ToList());
            }

            MethodEnd = _instructions.LastOrDefault().GetNextInstructionAddress();
            if (MethodEnd == 0) MethodEnd = methodPointer;
        }

        internal AsmAnalyzerBase(MethodDefinition definition, ulong methodPointer, IList<T> instructions, BaseKeyFunctionAddresses baseKeyFunctionAddresses) : this(methodPointer, instructions, baseKeyFunctionAddresses)
        {
            Instructions = instructions;
            MethodDefinition = definition;
            MethodDefinition.Body = new(MethodDefinition);
            IsGenuineMethod = true;
            Analysis = new(definition, methodPointer, MethodEnd, _instructions);
            Analysis.OnExpansionRequested += AnalysisRequestedExpansion;
        }

        public StringBuilder GetWordyFunctionality()
        {
            var builder = new StringBuilder();

            builder.Append($"\n\tMethod Synopsis For {(MethodDefinition?.IsStatic == true ? "Static " : "")}Method ")
                .Append(MethodDefinition?.FullName ?? "[unknown name]")
                .Append(":\n").Append((object)_methodFunctionality)
                .Append("\n\n");

            return builder;
        }

        public StringBuilder GetPseudocode()
        {
            var builder = new StringBuilder();

            builder.Append("\n\tGenerated Pseudocode:\n\n");

            //Preamble
            builder.Append($"\tDeclaring Type: {MethodDefinition?.DeclaringType.FullName ?? "unknown"}\n");
            builder.Append('\t').Append(MethodDefinition?.IsStatic == true ? "static " : "").Append(MethodDefinition?.ReturnType.FullName).Append(' ') //Staticness and return type
                .Append(MethodDefinition?.Name).Append('(') //Name and opening paranthesis
                .Append(string.Join(", ", MethodDefinition?.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}") ?? new List<string>())) //Parameters
                .Append(')').Append('\n'); //Closing parenthesis and new line.

            //Actions
            Analysis.Actions
                .Where(action => action.IsImportant()) //Action requires pseudocode generation
                .Select(action => $"{(action.PseudocodeNeedsLinebreakBefore() ? "\n" : "")}\t\t{"    ".Repeat(action.IndentLevel)}{action.ToPsuedoCode()?.Replace("\n", "\n" + "    ".Repeat(action.IndentLevel + 2))}") //Generate it 
                .Where(code => !string.IsNullOrWhiteSpace(code)) //Check it's valid
                .ToList()
                .ForEach(code => builder.Append(code).Append('\n')); //Append

            builder.Append("\n\n");

            return builder;
        }

        public StringBuilder BuildILToString()
        {
            var builder = new StringBuilder();

            //IL Generation
            //Anyone reading my commits: This is a *start*. It's nowhere near done.
            var body = MethodDefinition!.Body;
            var processor = body.GetILProcessor();

            var originalBody = body.Instructions.ToList();
            var originalVariables = body.Variables.ToList();

            processor.Clear();

            builder.Append("Generated IL:\n\t");

            var success = true;

            foreach (var localDefinition in Analysis.Locals.Where(localDefinition => localDefinition.ParameterDefinition == null && localDefinition.Type != null))
            {
                var varType = localDefinition.Type!;

                try
                {
                    if (varType is GenericInstanceType git2 && git2.HasAnyGenericParams())
                        varType = git2.Resolve();
                    if (varType is GenericInstanceType git)
                        varType = processor.ImportRecursive(git, MethodDefinition);

                    localDefinition.Variable = new VariableDefinition(processor.ImportReference(varType, MethodDefinition));
                    body.Variables.Add(localDefinition.Variable);
                }
                catch (InvalidOperationException)
                {
                    Logger.WarnNewline($"Skipping IL Generation for {MethodDefinition}, as one of its locals, {localDefinition.Name}, has a type, {varType}, which is invalid for use in a variable.", "Analysis");
                    builder.Append($"IL Generation Skipped due to invalid local {localDefinition.Name} of type {localDefinition.Type}\n\t");
                    success = false;
                    break;
                }
            }

            if (success)
            {
                foreach (var action in Analysis.Actions.Where(i => i.IsImportant()))
                {
                    try
                    {
                        var il = action.ToILInstructions(Analysis, processor);

                        foreach (var instruction in il)
                        {
                            processor.Append(instruction);
                        }

                        if (MethodAnalysis<Instruction>.ActionsWhichGenerateNoIL.Contains(action.GetType()) || il.Length == 0)
                            continue;

                        var jumpsToHere = Analysis.JumpTargetsToFixByAction.Keys.Where(jt => jt.AssociatedInstruction.GetInstructionAddress() <= action.AssociatedInstruction.GetInstructionAddress()).ToList();
                        if (jumpsToHere.Count > 0)
                        {
                            var first = il.First();
                            foreach (var instruction in jumpsToHere.SelectMany(jumpDestAction => Analysis.JumpTargetsToFixByAction[jumpDestAction]))
                            {
                                instruction.Operand = first;
                            }
                        }

                        jumpsToHere.ForEach(key => Analysis.JumpTargetsToFixByAction.Remove(key));
                    }
                    catch (NotImplementedException)
                    {
                        builder.Append($"Don't know how to write IL for {action.GetType()}.");

                        if (!Cpp2IlApi.IlContinueThroughErrors)
                        {
                            builder.Append(" Aborting here.");
                            builder.Append('\n');
                            success = false;
                            break;
                        }
                        builder.Append("\n\t");
                    }
                    catch (TaintedInstructionException e)
                    {
                        var message = e.ActualMessage ?? "No further info";
                        builder.Append($"Action of type {action.GetType()} at (0x{action.AssociatedInstruction.GetInstructionAddress():X}) is corrupt ({message}) and cannot be created as IL.");
                        if (!Cpp2IlApi.IlContinueThroughErrors)
                        {
                            builder.Append(" Aborting here.");
                            builder.Append('\n');
                            success = false;
                            break;
                        }
                        builder.Append("\n\t");
                    }
                    catch (Exception e)
                    {
                        Logger.WarnNewline($"Exception generating IL for {MethodDefinition.FullName}, thrown by action {action.GetType().Name}, associated instruction {action.AssociatedInstruction}: {e}");
                        builder.Append($"Action of type {action.GetType()} threw an exception while generating IL.");
                        if (!Cpp2IlApi.IlContinueThroughErrors)
                        {
                            builder.Append(" Aborting here.");
                            builder.Append('\n');
                            success = false;
                            break;
                        }
                        builder.Append("\n\t");
                    }
                }
            }

            if (body.Variables.Any(l => l.VariableType is GenericParameter { Position: -1 }))
                //don't save to body if any locals are screwed.
                success = false;

            if (!success)
            {
                body.Variables.Clear();
                processor.Clear();
                originalVariables.ForEach(body.Variables.Add);
                originalBody.ForEach(processor.Append);
            }
            else
            {
                body.Optimize();

                builder.Append(string.Join("\n\t", body.Instructions))
                    .Append("\n\t");
            }

            if (IsGenuineMethod)
            {
                if (success)
                    AsmAnalyzerX86.SUCCESSFUL_METHODS++;
                else
                    AsmAnalyzerX86.FAILED_METHODS++;
            }

            builder.Append("\n\n");

            return builder;
        }

        public void BuildMethodFunctionality()
        {
            _methodFunctionality.Append($"\t\tEnd of function at 0x{MethodEnd:X}\n\t\tAbsolute End is at 0x{Analysis.AbsoluteMethodEnd:X}\n");

            _methodFunctionality.Append("\t\tIdentified Jump Destination addresses:\n").Append(string.Join("\n", Analysis.IdentifiedJumpDestinationAddresses.Select(s => $"\t\t\t0x{s:X}"))).Append('\n');
            var lastIfAddress = 0UL;
            foreach (var action in Analysis.Actions)
            {
                if (Analysis.IdentifiedJumpDestinationAddresses.FirstOrDefault(s => s <= action.AssociatedInstruction.GetInstructionAddress() && s > lastIfAddress) is var jumpDestinationAddress && jumpDestinationAddress != 0)
                {
                    var associatedIfForThisElse = Analysis.GetAddressOfAssociatedIfForThisElse(jumpDestinationAddress);
                    var elseStart = Analysis.GetAddressOfElseThisIsTheEndOf(jumpDestinationAddress);
                    var ifStart = Analysis.GetAddressOfIfBlockEndingHere(jumpDestinationAddress);
                    if (associatedIfForThisElse != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tElse Block (starting at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") for Comparison at 0x")
                            .Append(associatedIfForThisElse.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else if (elseStart != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tEnd Of If-Else Block (at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") where the else started at 0x")
                            .Append(elseStart.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else if (ifStart != 0UL)
                    {
                        _methodFunctionality.Append("\n\t\tEnd Of If Block (at 0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append(") where the if started at 0x")
                            .Append(ifStart.ToString("x8").ToUpperInvariant())
                            .Append('\n');
                    }
                    else
                    {
                        _methodFunctionality.Append("\n\t\tJump Destination (0x")
                            .Append(jumpDestinationAddress.ToString("x8").ToUpperInvariant())
                            .Append("):\n");
                    }

                    lastIfAddress = jumpDestinationAddress;
                }

                if (Analysis.ProbableLoopStarts.FirstOrDefault(s => s <= action.AssociatedInstruction.GetInstructionAddress() && s > lastIfAddress) is { } loopAddress && loopAddress != 0)
                {
                    _methodFunctionality.Append("\n\t\tPotential Loop Start (0x")
                        .Append(loopAddress.ToString("x8").ToUpperInvariant())
                        .Append("):\n");

                    lastIfAddress = loopAddress;
                }

                string synopsisEntry;
                try
                {
                    synopsisEntry = action.GetSynopsisEntry();
                }
                catch (Exception e)
                {
                    Logger.WarnNewline($"Failed to generate synopsis for method {MethodDefinition?.FullName}, action of type {action.GetType().Name} for instruction {action.AssociatedInstruction} at 0x{action.AssociatedInstruction.GetInstructionAddress():X} - got exception {e}");
                    throw new AnalysisExceptionRaisedException("Exception generating synopsis entry", e);
                }

                if (!string.IsNullOrWhiteSpace(synopsisEntry))
                {
                    _methodFunctionality.Append("\t\t0x")
                        .Append(action.AssociatedInstruction.GetInstructionAddress().ToString("X8").ToUpperInvariant())
                        .Append(": ")
                        .Append(action.GetSynopsisEntry())
                        .Append('\n');
                }
            }
        }

        internal void AddParameter(TypeDefinition type, string name)
        {
            Analysis.AddParameter(new(name, ParameterAttributes.None, type));
        }

        public StringBuilder GetFullDumpNoIL()
        {
            var builder = new StringBuilder();

            builder.Append(GetAssemblyDump());
            builder.Append(GetWordyFunctionality());
            builder.Append(GetPseudocode());

            return builder;
        }

        /// <summary>
        /// Performs analysis in order to populate the Action list. Doesn't generate any text. 
        /// </summary>
        /// <exception cref="AnalysisExceptionRaisedException">If an unhandled exception occurs while analyzing.</exception>
        public void AnalyzeMethod()
        {
            //Main instruction loop
            for (var index = 0; index < _instructions.Count; index++)
            {
                var instruction = _instructions[index];
                try
                {
                    PerformInstructionChecks(instruction);
                }
                catch (Exception e)
                {
                    Logger.WarnNewline($"Failed to perform analysis on method {MethodDefinition?.FullName}\nWhile analysing instruction {instruction} at 0x{instruction.GetInstructionAddress():X}\nGot exception: {e}\n", "Analyze");
                    throw new AnalysisExceptionRaisedException("Internal analysis exception", e);
                }
            }
        }

        protected abstract bool FindInstructionWhichOverran(out int idx);

        protected abstract void AnalysisRequestedExpansion(ulong ptr);

        internal abstract StringBuilder GetAssemblyDump();

        public abstract void RunPostProcessors();

        protected abstract void PerformInstructionChecks(T instruction);
    }
}