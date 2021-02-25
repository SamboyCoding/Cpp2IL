//Feature flags

// #define DEBUG_PRINT_OPERAND_DATA

#define USE_NEW_ANALYSIS_METHOD

#if !USE_NEW_ANALYSIS_METHOD
using SharpDisasm;
using System.Globalization;
using System.Text.RegularExpressions;
using Instruction = SharpDisasm.Instruction;
using System;
using System.Collections.Concurrent;
#else
using LibCpp2IL.Metadata;
using Cpp2IL.Analysis.Actions;
using Iced.Intel;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Analysis.Actions.Important;
using Cpp2IL.Analysis.PostProcessActions;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.PE;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SharpDisasm.Udis86;
using Instruction = Iced.Intel.Instruction;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Cpp2IL.Analysis
{
    internal partial class AsmDumper
    {
        private static readonly List<ulong> _functionAddresses = SharedState.MethodsByAddress.Keys.ToList();

        private readonly MethodDefinition _methodDefinition;
        private readonly ulong _methodStart;
        private ulong _methodEnd;
        private readonly KeyFunctionAddresses _keyFunctionAddresses;
        private readonly PE _cppAssembly;

        private readonly StringBuilder _methodFunctionality = new StringBuilder();
#if !USE_NEW_ANALYSIS_METHOD
        private List<Instruction> _instructions;
#else
        private readonly InstructionList _instructions;
        private readonly Mnemonic[] MNEMONICS_INDICATING_CONSTANT_IS_NOT_CONSTANT =
        {
            Mnemonic.Add, Mnemonic.Sub
        };
#endif

        internal readonly MethodAnalysis Analysis;

#if !USE_NEW_ANALYSIS_METHOD
        private List<string> _loopRegisters = new List<string>();

        private ConcurrentDictionary<string, string> _registerAliases = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, TypeDefinition> _registerTypes = new ConcurrentDictionary<string, TypeDefinition>();
        private ConcurrentDictionary<string, object> _registerContents = new ConcurrentDictionary<string, object>();
        private StringBuilder _psuedoCode = new StringBuilder();
        private StringBuilder _typeDump = new StringBuilder();

        private int _blockDepth;
        private int _localNum;

        private Tuple<(string, TypeDefinition, object), (string, TypeDefinition?, object?)>? _lastComparison;
        private List<int> _indentCounts = new List<int>();
        private Stack<PreBlockCache> _savedRegisterStates = new Stack<PreBlockCache>();
        
        private Dictionary<int, string> _stackAliases = new Dictionary<int, string>();
        private Dictionary<int, TypeDefinition> _stackTypes = new Dictionary<int, TypeDefinition>();

        private TaintReason _taintReason = TaintReason.UNTAINTED;

        private BlockType _currentBlockType = BlockType.NONE;

        private readonly List<ulong> unknownMethodAddresses = new List<ulong>();
        
        private TypeDefinition? interfaceOffsetTargetInterface;

        private static readonly TypeDefinition TypeReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Type")!;
        private static readonly TypeDefinition StringReference = Utils.TryLookupTypeDefKnownNotGeneric("System.String")!;
        private static readonly TypeDefinition BooleanReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Boolean")!;
        private static readonly TypeDefinition FloatReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Single")!;
        private static readonly TypeDefinition ByteReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Byte")!;
        private static readonly TypeDefinition ShortReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Int16")!;
        private static readonly TypeDefinition IntegerReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Int32")!;
        private static readonly TypeDefinition LongReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Int64")!;
        private static readonly TypeDefinition ArrayReference = Utils.TryLookupTypeDefKnownNotGeneric("System.Array")!;

        private static readonly ConcurrentDictionary<ulong, TypeDefinition> exceptionThrowerAddresses = new ConcurrentDictionary<ulong, TypeDefinition>();
        
        private readonly ud_mnemonic_code[] _inlineArithmeticOpcodes =
        {
            ud_mnemonic_code.UD_Imul, //Multiply
            ud_mnemonic_code.UD_Iimul, //Signed multiply
            ud_mnemonic_code.UD_Imulss, //Multiply Scalar Single
            ud_mnemonic_code.UD_Isubss, //Subtract Scalar Single
            ud_mnemonic_code.UD_Isub, //Subtract
            ud_mnemonic_code.UD_Iadd, //Add
        };

        private readonly ud_mnemonic_code[] _localCreatingArithmeticOpcodes =
        {
            ud_mnemonic_code.UD_Isar, //Shift, Arithmetic, Right
            ud_mnemonic_code.UD_Ishr, //Shift right (logical)
        };

        private readonly ud_mnemonic_code[] _moveOpcodes =
        {
            ud_mnemonic_code.UD_Imov, //General move
            ud_mnemonic_code.UD_Imovaps,
            ud_mnemonic_code.UD_Imovss, //Move scalar single
            ud_mnemonic_code.UD_Imovsd, //Move scalar double
            ud_mnemonic_code.UD_Imovzx, //Move negated
            ud_mnemonic_code.UD_Ilea, //Load effective address
            ud_mnemonic_code.UD_Imovups
        };
#endif

        internal AsmDumper(MethodDefinition methodDefinition, CppMethodData method, ulong methodStart, KeyFunctionAddresses keyFunctionAddresses, PE cppAssembly)
        {
            _methodDefinition = methodDefinition;
            _methodStart = methodStart;

            _methodDefinition.Body = new MethodBody(_methodDefinition);

            _keyFunctionAddresses = keyFunctionAddresses;
            _cppAssembly = cppAssembly;

            //Pass 0: Disassemble
#if USE_NEW_ANALYSIS_METHOD
            _instructions = LibCpp2ILUtils.DisassembleBytesNew(LibCpp2IlMain.ThePe!.is32Bit, method.MethodBytes, methodStart);

            var instructionWhichOverran = new Instruction();
            var idx = 1;
            foreach (var i in _instructions.Skip(1))
            {
                idx++;
                if (SharedState.MethodsByAddress.ContainsKey(i.IP))
                {
                    instructionWhichOverran = i;
                    break;
                }
            }

            if (instructionWhichOverran != default)
            {
                _instructions = new InstructionList(_instructions.Take(idx).ToList());
            }

            _methodEnd = _instructions.LastOrDefault().NextIP;
            if (_methodEnd == 0) _methodEnd = _methodStart;

            Analysis = new MethodAnalysis(_methodDefinition, _methodStart, _methodEnd, _instructions);
#else
            _instructions = Utils.DisassembleBytes(LibCpp2IlMain.ThePe.is32Bit, method.MethodBytes);
#endif
        }

#if !USE_NEW_ANALYSIS_METHOD
        private void TaintMethod(TaintReason reason)
        {
            if (_taintReason != TaintReason.UNTAINTED) return;

            _taintReason = reason;
            _typeDump.Append($" ; !!! METHOD TAINTED HERE: {reason} (COMPLEXITY {(int) reason}) !!!");
            _psuedoCode.Append($"{Utils.Repeat("\t", _blockDepth)}//!!!METHOD TAINTED HERE: {reason} (COMPLEXITY {(int) reason})!!!\n");
            _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}!!! METHOD TAINTED HERE: {reason} (COMPLEXITY {(int) reason}) !!!\n");
        }
        
        private List<Instruction> TrimOutIl2CppCrap(List<Instruction> instructions)
        {
            var ret = new List<Instruction>();

            for (var i = 0; i < instructions.Count; i++)
            {
                if (Utils.CheckForNullCheckAtIndex(_methodStart, _cppAssembly, instructions, i, _keyFunctionAddresses))
                {
                    //Skip this (TEST) and the following instruction (JZ)
                    i++;
                    continue;
                }

                var toSkip = Utils.CheckForInitCallAtIndex(_methodStart, instructions, i, _keyFunctionAddresses);
                if (toSkip != 0)
                {
                    //Skip however many instructions
                    i += toSkip;
                    continue;
                }

                toSkip = Utils.CheckForStaticClassInitAtIndex(_methodStart, instructions, i, _keyFunctionAddresses);
                if (toSkip != 0)
                {
                    //No need to preserve anything, just skip
                    i += toSkip;
                    continue;
                }

                var insn = instructions[i];

                try
                {
                    //Single call to the bailout, sometimes left over at the end of a function
                    if (insn.Mnemonic == ud_mnemonic_code.UD_Icall && Utils.GetJumpTarget(insn, _methodStart + insn.PC) == _keyFunctionAddresses.AddrBailOutFunction)
                        continue;
                }
                catch (Exception)
                {
                    // ignored
                }

                //Need to clean up the syntax around native method lookups as it confuses the lexer
                //What it currently does is reads from a global storing the cached address of the function
                //And if it's not zero jumps over the lookup, straight to the call
                //And if it IS zero continues on to the lookup, a write to that global, and then the call.

                //To detect this, we look for a MOV from a relative offset from rip, into rax (i THINK it's always rax)
                //This is followed by a test against itself (checking for zero), then a jnz
                //Then it LEAs the literal name of the function (this we want to keep)
                //Then calls the native lookup (keep this too)
                //Then tests rax against itself again to check it was able to resolve the function
                //Then JZs down to a block that bails out 
                //Then moves the address to the global cache variable
                //Then we have the call, and this onwards we preserve.

                if (insn.Mnemonic == ud_mnemonic_code.UD_Imov && insn.Operands[1].Base == ud_type.UD_R_RIP && instructions.Count - i > 7)
                {
                    var requiredPattern = new[]
                    {
                        ud_mnemonic_code.UD_Imov, ud_mnemonic_code.UD_Itest, ud_mnemonic_code.UD_Ijnz, ud_mnemonic_code.UD_Ilea, ud_mnemonic_code.UD_Icall, ud_mnemonic_code.UD_Itest, ud_mnemonic_code.UD_Ijz, ud_mnemonic_code.UD_Imov
                    };
                    var patternInstructions = instructions.Skip(i).Take(8).ToArray();
                    var actualPattern = patternInstructions.Select(inst => inst.Mnemonic).ToArray();
                    // Console.WriteLine($"Expecting {string.Join(",", requiredPattern)}, got {string.Join(",", actualPattern)}");
                    if (actualPattern.SequenceEqual(requiredPattern))
                    {
                        //We need to preserve i+3 and i+4
                        ret.Add(instructions[i + 3]);
                        ret.Add(instructions[i + 4]);
                        i += 7;
                        continue;
                    }
                }

                ret.Add(insn);
            }

            return ret;
        }
#endif

        internal TaintReason AnalyzeMethod(StringBuilder typeDump, ref List<ud_mnemonic_code> allUsedMnemonics)
        {
            //On windows x86_64, function params are passed RCX RDX R8 R9, then the stack
            //If these are floating point numbers, they're put in XMM0 to 3
            //Register eax/rax/whatever you want to call it is the return value (both of any functions called in this one and this function itself)

            typeDump.Append($"Method: {_methodDefinition.FullName}:");

#if !USE_NEW_ANALYSIS_METHOD
            _typeDump = typeDump;
            //Map of jumped-to addresses to functionality summaries (for if statements)
            var jumpTable = new Dictionary<ulong, List<string>>();

            //Pass 1: Removal of unneeded generated code
            _instructions = TrimOutIl2CppCrap(_instructions);

            //Prevent overrunning into another function
            var lastInstructionInThisMethod = _instructions.FindIndex(i => SharedState.MethodsByAddress.ContainsKey(i.PC + _methodStart));

            if (lastInstructionInThisMethod > 0)
            {
                _instructions = _instructions.Take(lastInstructionInThisMethod + 2).ToList();
            }

            //Int3 is padding, we stop at the first one.
            var idx = _instructions.FindIndex(i => i.Mnemonic == ud_mnemonic_code.UD_Iint3);
            if (idx > 0 && idx < _instructions.Count - 2 && _instructions[idx + 2].Mnemonic == ud_mnemonic_code.UD_Iint3)
                _instructions = _instructions.Take(idx + 2).ToList();

            var distinctMnemonics = new List<ud_mnemonic_code>(_instructions.Select(i => i.Mnemonic).Distinct());
            allUsedMnemonics = new List<ud_mnemonic_code>(allUsedMnemonics.Concat(distinctMnemonics).Distinct());

            //Do this AFTER the trim so we get a better result 
            _methodEnd = _methodStart + (_instructions.LastOrDefault()?.PC ?? 0);

            //Pass 2: Early Loop Detection
            _loopRegisters = DetectPotentialLoops(_instructions);

            var counterNum = 1;
            var loopDetails = new List<string>();

            //Define counter variables for all loop registers
            foreach (var loopRegister in _loopRegisters)
            {
                _registerAliases[loopRegister] = $"counter{counterNum}";
                _registerTypes[loopRegister] = IntegerReference;
                loopDetails.Add($"counter{counterNum} in {loopRegister}");
                counterNum++;
            }

            //Flag up any loops we found in the summary
            if (_loopRegisters.Count > 0)
                _methodFunctionality.Append($"\t\tPotential Loops: {string.Join(",", loopDetails)}\n");

            //Dump params
            typeDump.Append("\tParameters in registers: \n");

            var registers = new List<string>(new[] {"rcx/xmm0", "rdx/xmm1", "r8/xmm2", "r9/xmm3"});
            var stackIndex = 0;

            //If not static add "this" reference to params
            if (!_methodDefinition.IsStatic)
            {
                var pos = registers[0];
                registers.RemoveAt(0);
                typeDump.Append($"\t\t{_methodDefinition.DeclaringType} this in register {pos}\n");
                foreach (var reg in pos.Split('/'))
                {
                    _registerAliases[reg] = "this";
                    _registerTypes[reg] = _methodDefinition.DeclaringType;
                }
            }

            //Handle all actual params
            foreach (var parameter in _methodDefinition.Parameters)
            {
                object pos;
                var isReg = false;
                if (registers.Count > 0)
                {
                    var regPair = registers[0];
                    pos = regPair;
                    isReg = true;
                    registers.RemoveAt(0);
                    foreach (var reg in regPair.Split('/'))
                    {
                        _registerAliases[reg] = parameter.Name;
                        _registerTypes[reg] = Utils.TryLookupTypeDefByName(parameter.ParameterType.FullName).Item1;
                        if (parameter.ParameterType.IsArray)
                        {
                            _registerTypes[reg] = parameter.ParameterType.GetElementType().Resolve();
                            _registerContents[reg] = new ArrayData(int.MaxValue, _registerTypes[reg]);
                        }
                    }
                }
                else
                {
                    pos = stackIndex.ToString();
                    stackIndex += 8; //Pointer.
                }

                typeDump.Append($"\t\t{parameter.ParameterType.FullName} {parameter.Name} in {(isReg ? $"register {pos}" : $"stack at 0x{pos:X}")}\n");
            }
            _localNum = 1;
            _lastComparison = new Tuple<(string, TypeDefinition, object), (string, TypeDefinition, object)>(("", null, null), ("", null, null));
#endif

            typeDump.Append("\tMethod Body (x86 ASM):\n");

            var index = 0;

            _methodFunctionality.Append($"\t\tEnd of function at 0x{_methodEnd:X}\n");

            //Main instruction loop
            while (index < _instructions.Count)
            {
                var instruction = _instructions[index];
                index++;

#if !USE_NEW_ANALYSIS_METHOD
                _blockDepth = _indentCounts.Count;
                string line;
                //SharpDisasm is a godawful library, and it's not threadsafe (but only for instruction tostrings), but it's the best we've got. So don't do this in parallel.
                lock (Disassembler.Translator)
                    line = instruction.ToString();
#else
                var line = new StringBuilder();
                line.Append("0x").Append(instruction.IP.ToString("X8").ToUpperInvariant()).Append(' ').Append(instruction);

                //Dump debug data
#if DEBUG_PRINT_OPERAND_DATA
                line.Append("\t\t; DEBUG: {").Append(instruction.Op0Kind).Append('}').Append('/').Append(instruction.Op0Register).Append(' ');
                line.Append('{').Append(instruction.Op1Kind).Append('}').Append('/').Append(instruction.Op1Register).Append(" ||| ");
                line.Append(instruction.MemoryBase).Append(" | ").Append(instruction.MemoryDisplacement).Append(" | ").Append(instruction.MemoryIndex);
#endif
#endif

                //I'm doing this here because it saves a bunch of effort later. Upscale all registers from 32 to 64-bit accessors. It's not correct, but it's simpler.
                // line = Utils.UpscaleRegisters(line);
#if !USE_NEW_ANALYSIS_METHOD
                //Apply any aliases to the line
                line = _registerAliases.Aggregate(line, (current, kvp) => current.Replace($" {kvp.Key}", $" {kvp.Value}_{kvp.Key}").Replace($"[{kvp.Key}", $"[{kvp.Value}_{kvp.Key}"));
#endif

                typeDump.Append("\t\t").Append(line); //write the current disassembled instruction to the type dump

#if DEBUG_PRINT_OPERAND_DATA
                typeDump.Append(" ; ");
                typeDump.Append(string.Join(" | ", instruction.Operands.Select((op, pos) => $"Op{pos}: {op.Type}")));
#endif

                try
                {
                    PerformInstructionChecks(instruction);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to perform analysis on method {_methodDefinition.FullName}, instruction {instruction} at 0x{instruction.IP:X} - got exception {e}");
                    throw new AnalysisExceptionRaisedException("Internal analysis exception", e);
                }

                typeDump.Append("\n");
#if !USE_NEW_ANALYSIS_METHOD
                if (instruction.Mnemonic == ud_mnemonic_code.UD_Iret && _blockDepth == 0)
                    break; //Can't continue

                var old = _indentCounts.Count;

                _indentCounts = _indentCounts
                    .Select(i => i - 1)
                    .Where(i => i > 0)
                    .ToList();

                if (_indentCounts.Count < old)
                {
                    var count = old - _indentCounts.Count;

                    while (count > 0)
                    {
                        PopBlock();
                        count--;
                    }
                }
#endif
            }

#if USE_NEW_ANALYSIS_METHOD
            new RemovedUnusedLocalsPostProcessor().PostProcess(Analysis, _methodDefinition);

            _methodFunctionality.Append("\t\tIdentified Jump Destination addresses:\n").Append(string.Join("\n", Analysis.IdentifiedJumpDestinationAddresses.Select(s => $"\t\t\t0x{s:X}"))).Append('\n');
            var lastIfAddress = 0UL;
            foreach (var action in Analysis.Actions)
            {
                if (Analysis.IdentifiedJumpDestinationAddresses.FirstOrDefault(s => s <= action.AssociatedInstruction.IP && s > lastIfAddress) is { } jumpDestinationAddress && jumpDestinationAddress != 0)
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

                if (Analysis.ProbableLoopStarts.FirstOrDefault(s => s <= action.AssociatedInstruction.IP && s > lastIfAddress) is { } loopAddress && loopAddress != 0)
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
                    Console.WriteLine($"Failed to generate synopsis for method {_methodDefinition.FullName}, instruction {action.AssociatedInstruction} at 0x{action.AssociatedInstruction.IP:X} - got exception {e}");
                    throw new AnalysisExceptionRaisedException("Exception generating synopsis entry", e);
                }

                if (!string.IsNullOrWhiteSpace(synopsisEntry))
                {
                    _methodFunctionality.Append("\t\t0x")
                        .Append(action.AssociatedInstruction.IP.ToString("X8").ToUpperInvariant())
                        .Append(": ")
                        .Append(action.GetSynopsisEntry())
                        .Append('\n');
                }
            }
            // _methodFunctionality.Append(string.Join("\n", _analysis.Actions.Select(a => $"\t\t0x{a.AssociatedInstruction.IP.ToString("X8").ToUpperInvariant()}: {a.GetSynopsisEntry()}")));
#endif

            // Console.WriteLine("Processed " + _methodDefinition.FullName);
            typeDump.Append($"\n\tMethod Synopsis For {(_methodDefinition.IsStatic ? "Static " : "")}Method ").Append(_methodDefinition.FullName).Append(":\n").Append(_methodFunctionality).Append("\n\n");

            typeDump.Append("\n\tGenerated Pseudocode:\n\n");

            typeDump.Append($"\tDeclaring Type: {_methodDefinition.DeclaringType.FullName}\n");
            typeDump.Append('\t').Append(_methodDefinition.IsStatic ? "static " : "").Append(_methodDefinition.ReturnType.FullName).Append(' ') //Staticness and return type
                .Append(_methodDefinition.Name).Append('(') //Name and opening paranthesis
                .Append(string.Join(", ", _methodDefinition.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}"))) //Parameters
                .Append(')').Append('\n'); //Closing parenthesis and new line.

            Analysis.Actions
                .Where(action => action.IsImportant()) //Action requires pseudocode generation
                .Select(action => $"{(action.PseudocodeNeedsLinebreakBefore() ? "\n" : "")}\t\t{"    ".Repeat(action.IndentLevel)}{action.ToPsuedoCode()}") //Generate it 
                .Where(code => !string.IsNullOrWhiteSpace(code)) //Check it's valid
                .ToList()
                .ForEach(code => typeDump.Append(code).Append('\n')); //Append

            typeDump.Append("\n\n");

            //IL Generation
            //Anyone reading my commits: This is a *start*. It's nowhere near done.
            var body = _methodDefinition.Body;
            var processor = body.GetILProcessor();

            typeDump.Append("Generated IL:\n\t");

            foreach (var localDefinition in Analysis.Locals.Where(localDefinition => localDefinition.ParameterDefinition == null))
            {
                localDefinition.Variable = new VariableDefinition(localDefinition.Type);
                body.Variables.Add(localDefinition.Variable);
            }

            foreach (var action in Analysis.Actions.Where(i => i.IsImportant()))
            {
                try
                {
                    var il = action.ToILInstructions(Analysis, processor);
                    typeDump.Append(string.Join("\n\t", il.AsEnumerable()))
                        .Append("\n\t");
                }
                catch (NotImplementedException)
                {
                    typeDump.Append($"Don't know how to write IL for {action.GetType()}. Aborting here.\n");
                    break;
                }
                catch (TaintedInstructionException)
                {
                    typeDump.Append($"Action of type {action.GetType()} is corrupt and cannot be created as IL. Aborting here.\n");
                    break;
                }
                catch (Exception e)
                {
                    typeDump.Append($"Action of type {action.GetType()} threw an exception while generating IL. Aborting here.\n");
                    Console.WriteLine(e);
                    break;
                }
            }

            typeDump.Append("\n\n");

#if !USE_NEW_ANALYSIS_METHOD
            return _taintReason;
#else
            return TaintReason.UNTAINTED;
#endif
        }

#if !USE_NEW_ANALYSIS_METHOD
        private void PushBlock(int toAdd, BlockType type)
        {
            _indentCounts.Add(toAdd);

            _savedRegisterStates.Push(new PreBlockCache(_registerAliases, _registerContents, _registerTypes, _currentBlockType));

            _currentBlockType = type;

            _blockDepth++;
        }

        private void PopBlock()
        {
            //Pop cached state
            var savedRegisterState = _savedRegisterStates.Pop();
            _registerAliases = savedRegisterState.Aliases;
            _registerContents = savedRegisterState.Constants;
            _registerTypes = savedRegisterState.Types;

            _currentBlockType = savedRegisterState.BlockType;

            _psuedoCode.Append(Utils.Repeat("\t", _blockDepth - 1)).Append("}").Append("\n");
            _blockDepth--;
        }

#else
        private void CheckForZeroOpInstruction(Instruction instruction)
        {
            switch (instruction.Mnemonic)
            {
                case Mnemonic.Ret:
                    Analysis.Actions.Add(new ReturnFromFunctionAction(Analysis, instruction));
                    break;
            }
        }

        private void CheckForSingleOpInstruction(Instruction instruction)
        {
            var reg = Utils.GetRegisterNameNew(instruction.Op0Register == Register.None ? instruction.MemoryBase : instruction.Op0Register);
            var operand = Analysis.GetOperandInRegister(reg);

            var memR = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var memOp = Analysis.GetOperandInRegister(memR);
            var memOffset = instruction.MemoryDisplacement;

            switch (instruction.Mnemonic)
            {
                //Note to self: (push [val]) is the same as (sub esp, 4) + (mov esp, [val])
                case Mnemonic.Push when instruction.Op0Kind == OpKind.Memory && instruction.Op0Register == Register.None && instruction.MemoryBase == Register.None:
                    //Push [Addr]
                    Analysis.Actions.Add(new PushGlobalAction(Analysis, instruction));
                    break;
                case Mnemonic.Push when instruction.Op0Kind == OpKind.Register && instruction.Op0Register != Register.None:
                    //Push [reg]
                    Analysis.Actions.Add(new PushRegisterAction(Analysis, instruction));
                    break;
                case Mnemonic.Push when instruction.Op0Kind.IsImmediate():
                    //Push constant
                    Analysis.Actions.Add(new PushConstantAction(Analysis, instruction));
                    break;
                case Mnemonic.Push when instruction.Op0Kind == OpKind.Memory && instruction.MemoryBase == Register.EBP:
                    //Push [EBP+x].
                    Analysis.Actions.Add(new PushEbpOffsetAction(Analysis, instruction));
                    break;
                //Likewise, (pop [val]) is the same as (mov [val], esp) + (add esp, 4)
                case Mnemonic.Pop:
                    break;
                case Mnemonic.Jmp when instruction.Op0Kind == OpKind.Memory && instruction.MemoryDisplacement == 0 && operand is ConstantDefinition callRegCons && callRegCons.Value is MethodDefinition:
                    Analysis.Actions.Add(new CallManagedFunctionInRegAction(Analysis, instruction));
                    //This is a jmp, so return
                    Analysis.Actions.Add(new ReturnFromFunctionAction(Analysis, instruction));
                    break;
                case Mnemonic.Jmp when instruction.Op0Kind != OpKind.Register:
                {
                    var jumpTarget = instruction.NearBranchTarget;
                    if (SharedState.MethodsByAddress.ContainsKey(jumpTarget))
                    {
                        //Call a managed function
                        Analysis.Actions.Add(new CallManagedFunctionAction(Analysis, instruction));
                        Analysis.Actions.Add(new ReturnFromFunctionAction(Analysis, instruction)); //JMP is an implicit return
                    }
                    else if (jumpTarget > instruction.IP && jumpTarget < Analysis.AbsoluteMethodEnd)
                    {
                        Analysis.Actions.Add(new JumpAlwaysAction(Analysis, instruction));
                    }
                    else if (jumpTarget > Analysis.MethodStart && jumpTarget < instruction.IP)
                    {
                        //Different from a normal jump because it's backwards, indicating a possible loop.
                        Analysis.Actions.Add(new JumpBackAction(Analysis, instruction));
                    }

                    break;
                }
                case Mnemonic.Call when instruction.Op0Kind == OpKind.Memory && instruction.MemoryDisplacement > 0x128 && operand is ConstantDefinition checkForVirtualCallCons &&
                                        checkForVirtualCallCons.Value is Il2CppClassIdentifier:
                    //Virtual method call
                    Analysis.Actions.Add(new CallVirtualMethodAction(Analysis, instruction));
                    break;
                case Mnemonic.Call when instruction.Op0Kind != OpKind.Register:
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    if (jumpTarget == _keyFunctionAddresses.AddrBailOutFunction)
                    {
                        //Bailout
                        Analysis.Actions.Add(new CallBailOutAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_codegen_initialize_method || jumpTarget == _keyFunctionAddresses.il2cpp_vm_metadatacache_initializemethodmetadata)
                    {
                        //Init method
                        Analysis.Actions.Add(new CallInitMethodAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_codegen_initialize_runtime_metadata && _keyFunctionAddresses.il2cpp_codegen_initialize_runtime_metadata != 0)
                    {
                        //Init runtime metadata
                        Analysis.Actions.Add(new InitializeRuntimeMetadataAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_runtime_class_init_actual || jumpTarget == _keyFunctionAddresses.il2cpp_runtime_class_init_export)
                    {
                        //Runtime class init
                        Analysis.Actions.Add(new CallInitClassAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_codegen_object_new || jumpTarget == _keyFunctionAddresses.il2cpp_vm_object_new)
                    {
                        //Allocate object
                        Analysis.Actions.Add(new AllocateInstanceAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_array_new_specific || jumpTarget == _keyFunctionAddresses.SzArrayNew || jumpTarget == _keyFunctionAddresses.il2cpp_vm_array_new_specific)
                    {
                        //Allocate array
                        Analysis.Actions.Add(new AllocateArrayAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.AddrNativeLookup)
                    {
                        //Lookup native method
                        Analysis.Actions.Add(new LookupNativeFunctionAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.AddrNativeLookupGenMissingMethod)
                    {
                        //Throw exception because we failed to find a native method
                        Analysis.Actions.Add(new CallNativeMethodFailureAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_value_box)
                    {
                        //Box a cpp primitive to a managed type
                        Analysis.Actions.Add(new BoxValueAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_object_is_inst)
                    {
                        //Safe cast an object to a type
                        Analysis.Actions.Add(new SafeCastAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_raise_managed_exception)
                    {
                        //Equivalent of a throw statement
                        Analysis.Actions.Add(new ThrowAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.AddrPInvokeLookup)
                    {
                        //P/Invoke lookup
                        //TODO work out how this works
                    }
                    else if (SharedState.MethodsByAddress.ContainsKey(jumpTarget))
                    {
                        //Call a managed function
                        Analysis.Actions.Add(new CallManagedFunctionAction(Analysis, instruction));
                    }
                    else if (CallExceptionThrowerFunction.IsExceptionThrower(jumpTarget))
                    {
                        Analysis.Actions.Add(new CallExceptionThrowerFunction(Analysis, instruction));
                    }
                    else if (Analysis.GetConstantInReg("rcx") is { } castConstant
                             && castConstant.Value is NewSafeCastResult //We have a cast result
                             && Analysis.GetConstantInReg("rdx") is { } interfaceConstant
                             && interfaceConstant.Value is TypeDefinition //we have a type
                             && Analysis.GetConstantInReg("r8") is { } slotConstant
                             && slotConstant.Value is int // We have a slot
                             && Analysis.Actions.Any(a => a is LocateSpecificInterfaceOffsetAction) //We've looked up an interface offset
                             && (!Analysis.Actions.Any(a => a is LoadInterfaceMethodDataAction) //Either we don't have any load method datas...
                                 || Analysis.Actions.FindLastIndex(a => a is LocateSpecificInterfaceOffsetAction) > Analysis.Actions.FindLastIndex(a => a is LoadInterfaceMethodDataAction))) //Or the last load offset is after the last load method data 
                    {
                        //Unknown function call but matches values for a Interface lookup
                        Analysis.Actions.Add(new LoadInterfaceMethodDataAction(Analysis, instruction));
                    }

                    break;
                }
                case Mnemonic.Inc when instruction.Op0Kind == OpKind.Register:
                    //Just add one.
                    Analysis.Actions.Add(new AddConstantToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Je when Analysis.Actions.LastOrDefault() is LocateSpecificInterfaceOffsetAction:
                    //Can be ignored.
                    break;
                case Mnemonic.Je:
                    Analysis.Actions.Add(new JumpIfZeroOrNullAction(Analysis, instruction));
                    break;
                case Mnemonic.Jne:
                    Analysis.Actions.Add(new JumpIfNonZeroOrNonNullAction(Analysis, instruction));
                    break;
                case Mnemonic.Ja:
                    //Jump if >
                    Analysis.Actions.Add(new JumpIfGreaterThanAction(Analysis, instruction));
                    break;
                case Mnemonic.Jge:
                case Mnemonic.Jae: //JNC in Ghidra
                    //Jump if >=
                    Analysis.Actions.Add(new JumpIfGreaterThanOrEqualToAction(Analysis, instruction));
                    break;
                case Mnemonic.Jle:
                case Mnemonic.Jbe:
                    //Jump if <=
                    Analysis.Actions.Add(new JumpIfLessThanOrEqualToAction(Analysis, instruction));
                    break;
                //TODO More Conditional jumps
                //Conditional boolean sets
                case Mnemonic.Setg:
                    Analysis.Actions.Add(new GreaterThanRegisterSetAction(Analysis, instruction));
                    break;
                //Floating-point unit (FPU) instructions
                case Mnemonic.Fld when memR != "rip" && memR != "rbp" && memOp is LocalDefinition:
                    //Load [addr] - which is a field address - onto the top of the floating point stack
                    Analysis.Actions.Add(new FieldToFpuStackAction(Analysis, instruction));
                    break;
                case Mnemonic.Fstp when memR == "rbp":
                    //Store the top of the floating point stack to an rbp-based [addr]
                    Analysis.Actions.Add(new FpuStackLocalToRbpOffsetAction(Analysis, instruction));
                    break;
            }
        }

        private void CheckForTwoOpInstruction(Instruction instruction)
        {
            var r0 = Utils.GetRegisterNameNew(instruction.Op0Register);
            var r1 = Utils.GetRegisterNameNew(instruction.Op1Register);
            var memR = Utils.GetRegisterNameNew(instruction.MemoryBase);

            var op0 = Analysis.GetOperandInRegister(r0);
            var op1 = Analysis.GetOperandInRegister(r1);
            var memOp = Analysis.GetOperandInRegister(memR);
            var memIdxOp = instruction.MemoryIndex == Register.None ? null : Analysis.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.MemoryIndex));

            var offset0 = instruction.MemoryDisplacement;
            var offset1 = offset0; //todo?

            var type0 = instruction.Op0Kind;
            var type1 = instruction.Op1Kind;

            switch (instruction.Mnemonic)
            {
                case Mnemonic.Mov when type0 == OpKind.Register && type1 == OpKind.Register && offset0 == 0 && offset1 == 0 && op1 != null:
                    //Both zero offsets and a known secondary operand = Register content copy
                    Analysis.Actions.Add(new RegToRegMoveAction(Analysis, instruction));
                    return;
                case Mnemonic.Mov when type1 == OpKind.Register && r0 == "rsp" && offset1 == 0 && op1 != null:
                    //Second operand is a reg, no offset, moving into the stack = Copy reg content to stack.
                    Analysis.Actions.Add(new StackToRegCopyAction(Analysis, instruction));
                    return;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && instruction.MemoryIndex == Register.None && memOp is ConstantDefinition constant && constant.Type == typeof(StaticFieldsPtr):
                    //Load a specific static field.
                    Analysis.Actions.Add(new StaticFieldToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && (offset0 == 0 || r0 == "rsp") && offset1 == 0 && memOp != null && instruction.MemoryIndex == Register.None:
                {
                    //Zero offsets, but second operand is a memory pointer -> class pointer move.
                    //MUST Check for non-cpp type
                    if (Analysis.GetLocalInReg(r1) != null)
                        Analysis.Actions.Add(new ClassPointerLoadAction(Analysis, instruction)); //We have a managed local type, we can load the class pointer for it
                    return;
                }
                //0xb0 == Il2CppRuntimeInterfaceOffsetPair* interfaceOffsets;
                case Mnemonic.Mov when type1 == OpKind.Memory && offset1 == 0xb0 && memOp is ConstantDefinition {Value: Il2CppClassIdentifier _}:
                    //Class pointer interface offset read
                    Analysis.Actions.Add(new InterfaceOffsetsReadAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && offset1 > 0x128 && memOp is ConstantDefinition {Value: Il2CppClassIdentifier _}:
                {
                    //Virtual method pointer load
                    Analysis.Actions.Add(new LoadVirtualFunctionPointerAction(Analysis, instruction));
                    break;
                }
                case Mnemonic.Lea when !_cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryBase == Register.RIP:
                case Mnemonic.Lea when _cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryBase == Register.None && instruction.MemoryDisplacement64 != 0:
                case Mnemonic.Mov when !_cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryBase == Register.RIP:
                case Mnemonic.Mov when _cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryDisplacement64 != 0 && instruction.MemoryBase == Register.None:
                {
                    //Global to stack or reg. Could be metadata literal, non-metadata literal, metadata type, or metadata method.
                    var globalAddress = _cppAssembly.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
                    if (LibCpp2IlMain.GetAnyGlobalByAddress(globalAddress) is { } global)
                    {
                        //Have a global here.
                        switch (global.Type)
                        {
                            case MetadataUsageType.Type:
                            case MetadataUsageType.TypeInfo:
                                Analysis.Actions.Add(new GlobalTypeRefToConstantAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.MethodDef:
                                Analysis.Actions.Add(new GlobalMethodDefToConstantAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.MethodRef:
                                Analysis.Actions.Add(new GlobalMethodRefToConstantAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.FieldInfo:
                                Analysis.Actions.Add(new GlobalFieldDefToConstantAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.StringLiteral:
                                Analysis.Actions.Add(new GlobalStringRefToConstantAction(Analysis, instruction));
                                break;
                        }
                    }
                    else
                    {
                        var potentialLiteral = Utils.TryGetLiteralAt(LibCpp2IlMain.ThePe!, (ulong) LibCpp2IlMain.ThePe.MapVirtualAddressToRaw(instruction.GetRipBasedInstructionMemoryAddress()));
                        if (potentialLiteral != null)
                        {
                            // if (r0 == "rsp")
                            //     _analysis.Actions.Add(new ConstantToStackAction(_analysis, instruction));
                            // else
                            //     _analysis.Actions.Add(new ConstantToRegAction(_analysis, instruction));
                        }
                        else
                        {
                            //Unknown global
                            Analysis.Actions.Add(new UnknownGlobalToConstantAction(Analysis, instruction));
                        }
                    }

                    return;
                }
                case Mnemonic.Mov when type1 >= OpKind.Immediate8 && type1 <= OpKind.Immediate32to64 && offset0 == 0 && type0 == OpKind.Register:
                    //Constant move to reg
                    var mayNotBeAConstant = MNEMONICS_INDICATING_CONSTANT_IS_NOT_CONSTANT.Any(m => _instructions.Any(i => i.Mnemonic == m && Utils.GetRegisterNameNew(i.Op0Register) != "rsp"));

                    Analysis.Actions.Add(new ConstantToRegAction(Analysis, instruction, mayNotBeAConstant));
                    return;
                case Mnemonic.Mov when type1 >= OpKind.Immediate8 && type1 <= OpKind.Immediate32to64 && offset0 != 0 && type0 == OpKind.Memory && instruction.MemoryBase == Register.None:
                    //Move constant to addr
                    Analysis.Actions.Add(new ConstantToGlobalAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1.IsImmediate() && offset0 != 0 && type0 != OpKind.Register && memR != "rip" && memR != "rbp":
                    //Constant move to field
                    Analysis.Actions.Add(new ConstantToFieldAction(Analysis, instruction));
                    return;
                //Note that, according to Il2CppArray class, an Il2Cpp Array has, after the core Object fields (klass and vtable)
                //an Il2CppArrayBounds object, which consists of a uintptr (the length) and an int (the "lower bound"), 
                //and then it has another uintptr representing the max length of the array.
                //So if we're accessing 0xC (32-bit) or 0x18 (64-bit) on an array - that's the length.
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && instruction.MemoryBase == Register.EBP:
                    //Read stack pointer to local
                    Analysis.Actions.Add(new EbpOffsetToLocalAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && instruction.MemoryBase == Register.EBP:
                    //Local to stack pointer (opposite of above)
                    Analysis.Actions.Add(new LocalToRbpOffsetAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && Analysis.GetConstantInReg(memR) != null && Il2CppClassUsefulOffsets.IsStaticFieldsPtr(instruction.MemoryDisplacement):
                    //Static fields ptr read
                    Analysis.Actions.Add(new StaticFieldOffsetToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is LocalDefinition local && local.Type?.IsArray == true:
                case Mnemonic.Lea when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is LocalDefinition local2 && local2.Type?.IsArray == true:
                    //Move reg, [reg+0x10]
                    //Reading a field from an array at a fixed offset
                    if (Il2CppArrayUtils.IsAtLeastFirstItemPtr(instruction.MemoryDisplacement))
                    {
                        Analysis.Actions.Add(new ConstantArrayOffsetToRegAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex != Register.None && memIdxOp is LocalDefinition local && local.Type?.IsArray == true:
                    //Move reg, [reg+reg] => usually array reads.
                    //So much so that this is guarded behind an array read check - change the case if you need to change this.
                    Analysis.Actions.Add(new RegOffsetArrayValueReadRegToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when !LibCpp2IlMain.ThePe!.is32Bit && type1 == OpKind.Memory && type0 == OpKind.Register && memR == "rsp" && instruction.MemoryIndex == Register.None:
                    //x64 Stack pointer read.
                    Analysis.Actions.Add(new StackOffsetReadX64Action(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None:
                    //Move generic memory to register - field read.
                    Analysis.Actions.Add(new FieldToLocalAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is ConstantDefinition {Value: StaticFieldsPtr _}:
                    //Write static field
                    Analysis.Actions.Add(new RegToStaticFieldAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is LocalDefinition {Type: ArrayType _} && Il2CppArrayUtils.IsAtLeastFirstItemPtr(instruction.MemoryDisplacement):
                    Analysis.Actions.Add(new RegToConstantArrayOffsetAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is LocalDefinition:
                    //Write non-static field
                    Analysis.Actions.Add(new LocalToFieldAction(Analysis, instruction));
                    break;
                //TODO Everything from CheckForFieldArrayAndStackReads
                //TODO More Arithmetic
                case Mnemonic.Add when type0 == OpKind.Register && type1 >= OpKind.Immediate8 && type1 <= OpKind.Immediate32to64 && r0 != "rsp":
                    //Add reg, val
                    Analysis.Actions.Add(new AddConstantToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Xor:
                case Mnemonic.Xorps:
                {
                    //PROBABLY clear register
                    if (r0 == r1)
                        Analysis.Actions.Add(new ClearRegAction(Analysis, instruction));
                    break;
                }
                case Mnemonic.Cmp when memOp is ConstantDefinition interfaceOffsetReadTestCons && interfaceOffsetReadTestCons.Value is Il2CppInterfaceOffset[]:
                    //Format is generally something like `cmp [r9+rax*8], r10`, where r9 is the interface offset array we have here, rax is the current loop index, and r10 is the target interface
                    if (instruction.MemoryIndexScale == 0x8 && op1 is ConstantDefinition interfaceOffsetReadTypeCons && interfaceOffsetReadTypeCons.Value is TypeDefinition)
                    {
                        Analysis.Actions.Add(new LocateSpecificInterfaceOffsetAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Test:
                case Mnemonic.Cmp:
                    //Condition
                    Analysis.Actions.Add(new ComparisonAction(Analysis, instruction));
                    break;
            }
        }

        private void PerformInstructionChecks(Instruction instruction)
        {
            var associatedIfFromIfElse = Analysis.GetAddressOfAssociatedIfForThisElse(instruction.IP);
            var associatedElse = Analysis.GetAddressOfElseThisIsTheEndOf(instruction.IP);
            var hasEndedLoop = Analysis.HaveWeExitedALoopOnThisInstruction(instruction.IP);
            var associatedIf = Analysis.GetAddressOfIfBlockEndingHere(instruction.IP);

            if (associatedIfFromIfElse != 0)
            {
                //We've just started an else block - pop the state from when we started the if.
                Analysis.PopStashedIfDataForElseAt(instruction.IP);

                Analysis.IndentLevel -= 1; //For the else statement
                Analysis.Actions.Add(new ElseMarkerAction(Analysis, instruction));
            }
            else if (associatedElse != 0)
            {
                Analysis.IndentLevel -= 1; //For the end if statement
                Analysis.Actions.Add(new EndIfMarkerAction(Analysis, instruction, true));
            }
            else if (hasEndedLoop)
            {
                Analysis.IndentLevel -= 1;
                Analysis.Actions.Add(new EndWhileMarkerAction(Analysis, instruction));
            }
            else if (associatedIf != 0)
            {
                Analysis.PopStashedIfDataFrom(associatedIf);

                Analysis.IndentLevel -= 1;
                Analysis.Actions.Add(new EndIfMarkerAction(Analysis, instruction, false));
            }

            var operandCount = instruction.OpCount;

            switch (operandCount)
            {
                case 0:
                    CheckForZeroOpInstruction(instruction);
                    return;
                case 1:
                    CheckForSingleOpInstruction(instruction);
                    return;
                case 2:
                    CheckForTwoOpInstruction(instruction);
                    return;
            }
        }
#endif

#if !USE_NEW_ANALYSIS_METHOD
        private void CheckForArithmeticOperations(Instruction instruction)
        {
            if (instruction.Mnemonic == ud_mnemonic_code.UD_Iimul && instruction.Operands.Length <= 3 && instruction.Operands.Length > 0)
            {
                string destReg;
                string firstArgName;
                string secondArgName;

                switch (instruction.Operands.Length)
                {
                    case 1:
                        destReg = "rdx";
                        if (!_registerAliases.TryGetValue("rax", out firstArgName))
                            firstArgName = $"[value in rax]";

                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);
                        break;
                    case 2:
                        destReg = Utils.GetRegisterName(instruction.Operands[0]);
                        (firstArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);
                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                        // firstSourceReg = GetRegisterName(instruction.Operands[0]);
                        // secondSourceReg = GetRegisterName(instruction.Operands[1]);
                        break;
                    case 3:
                        destReg = Utils.GetRegisterName(instruction.Operands[0]);
                        // firstSourceReg = GetRegisterName(instruction.Operands[1]);
                        // secondSourceReg = GetRegisterName(instruction.Operands[2]);
                        (firstArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                        (secondArgName, _, _) = GetDetailsOfReferencedObject(instruction.Operands[2], instruction);
                        break;
                }

                var localName = $"local{_localNum}";
                _localNum++;

                _registerAliases[destReg] = localName;

                _typeDump.Append("; - identified and processed one of them there godforsaken imul instructions.");
                _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(LongReference.FullName).Append(" ").Append(localName).Append(" = ").Append(firstArgName).Append(" * ").Append(secondArgName).Append("\n");
                _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}Multiplies {firstArgName} by {secondArgName} and stores the result in new local {localName} in register {destReg}\n");

                return;
            }

            //Need 2 operand
            if (instruction.Operands.Length < 2) return;

            //Needs to be a valid opcode
            if (!_inlineArithmeticOpcodes.Contains(instruction.Mnemonic) && !_localCreatingArithmeticOpcodes.Contains(instruction.Mnemonic)) return;

            var isInline = _inlineArithmeticOpcodes.Contains(instruction.Mnemonic);

            //The first operand is guaranteed to be a register
            var (name, type, _) = GetDetailsOfReferencedObject(instruction.Operands[0], instruction);

            //The second one COULD be a register, but it could also be a memory location containing a constant (say we're multiplying by 1.5, that'd be a constant)
            if (instruction.Operands[1].Type == ud_type.UD_OP_IMM || instruction.Operands[1].Type == ud_type.UD_OP_MEM && instruction.Operands[1].Base == ud_type.UD_R_RIP)
            {
                decimal constantValue;

                if (instruction.Operands[1].Type == ud_type.UD_OP_IMM)
                {
                    var immediateValue = Utils.GetImmediateValue(instruction, instruction.Operands[1]);
                    constantValue = immediateValue;
                    _typeDump.Append($" ; - Arithmetic operation between {name} and constant value: {constantValue}");
                }
                else
                {
                    var virtualAddress = Utils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]) + _methodStart;
                    _typeDump.Append($" ; - Arithmetic operation between {name} and constant stored at 0x{virtualAddress:X}");
                    long rawAddr = _cppAssembly.MapVirtualAddressToRaw(virtualAddress);
                    var constantBytes = _cppAssembly.raw.SubArray((int) rawAddr, 4);
                    var constantSingle = BitConverter.ToSingle(constantBytes, 0);
                    _typeDump.Append($" - constant is bytes: {BitConverter.ToString(constantBytes)}, or value {constantSingle}");
                    constantValue = (decimal) constantSingle;
                }

                if (isInline)
                {
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(constantValue).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) and {constantValue} (System.Decimal) in-place\n");
                }
                else
                {
                    var localName = $"local{_localNum}";
                    _localNum++;

                    var destReg = Utils.GetRegisterName(instruction.Operands[0]);

                    _registerAliases[destReg] = localName;
                    _registerTypes[destReg] = LongReference;
                    _registerContents.TryRemove(destReg, out _);

                    //System.Int64 localX = name [operation] constant\n
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(LongReference.FullName).Append(" ").Append(localName).Append(" = ").Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(constantValue).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) by {constantValue} (System.Decimal) and stores the result in new local {localName} in register {destReg}\n");
                }
            }
            else if (instruction.Operands[1].Type == ud_type.UD_OP_MEM || instruction.Operands[1].Type == ud_type.UD_OP_REG)
            {
                var (secondName, secondType, _) = GetDetailsOfReferencedObject(instruction.Operands[1], instruction);
                _typeDump.Append($" ; - Arithmetic operation between {name} and {secondName}");

                if (isInline)
                {
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(secondName).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) and {secondName} ({secondType}) in-place\n");
                }
                else
                {
                    var localName = $"local{_localNum}";
                    _localNum++;

                    var destReg = Utils.GetRegisterName(instruction.Operands[0]);

                    _registerAliases[destReg] = localName;
                    _registerTypes[destReg] = LongReference;
                    _registerContents.TryRemove(destReg, out _);

                    //System.Int64 localX = name [operation] constant\n
                    _psuedoCode.Append(Utils.Repeat("\t", _blockDepth)).Append(LongReference.FullName).Append(" ").Append(localName).Append(" = ").Append(name).Append(" ").Append(GetArithmeticOperationAssignment(instruction.Mnemonic)).Append(" ").Append(secondName).Append("\n");
                    _methodFunctionality.Append($"{Utils.Repeat("\t", _blockDepth + 2)}{GetArithmeticOperationName(instruction.Mnemonic)} {name} (type {type}) by {secondName} ({secondType?.FullName}) and stores the result in new local {localName} in register {destReg}\n");
                }
            }
            else
            {
                _typeDump.Append($" ; Arithmetic operation between {name} and [unimplemented handler for operand type {instruction.Operands[1].Type}]");
            }
        }

#endif

        private enum BlockType
        {
            NONE,
            IF,
            ELSE
        }

        internal enum TaintReason
        {
            UNTAINTED = 0,
            UNRESOLVED_METHOD = 1,
            UNRESOLVED_FIELD = 2,
            UNRESOLVED_STACK_VAL = 3,
            BAD_CONDITION = 4,
            METHOD_PARAM_MISSING = 5,
            NON_REMOVED_INTERNAL_FUNCTION = 6,
            FAILED_TYPE_RESOLVE = 7,
            METHOD_PARAM_MISMATCH = 8,
            FIELD_TYPE_MISMATCH = 9,
            NONSENSICAL_COMPARISON = 10,
            MISSING_IF_BODY = 11,
        }
    }
}