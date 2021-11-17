using System;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractComparisonAction<T> : BaseAction<T>
    {
        public IComparisonArgument? ArgumentOne;
        public IComparisonArgument? ArgumentTwo;
        
        private readonly string? ArgumentOneRegister;
        private readonly string? ArgumentTwoRegister;
        
        public bool UnimportantComparison;
        public ulong EndOfLoopAddr;

        protected AbstractComparisonAction(MethodAnalysis<T> context, T associatedInstruction, bool skipSecond = false) : base(context, associatedInstruction)
        {
            ArgumentOne = ExtractArgument(context, associatedInstruction, 0, out var unimportant1, out ArgumentOneRegister);

            if (ArgumentOne is ConstantDefinition { Value: UnknownGlobalAddr globalAddr } cons && ArgumentTwo is LocalDefinition { Type: { }, KnownInitialValue: null } loc2)
            {
                try
                {
                    MiscUtils.CoerceUnknownGlobalValue(loc2.Type, globalAddr, cons, false);
                    unimportant1 = false;
                }
                catch
                {
                    // ignored
                }
            }

            var unimportant2 = false;
            if (!skipSecond)
            {
                ArgumentTwo = ExtractArgument(context, associatedInstruction, 1, out unimportant2, out ArgumentTwoRegister);

                if (ArgumentTwo is ConstantDefinition { Value: UnknownGlobalAddr globalAddr2 } cons2 && ArgumentOne is LocalDefinition { Type: { }, KnownInitialValue: null } loc1)
                {
                    try
                    {
                        MiscUtils.CoerceUnknownGlobalValue(loc1.Type, globalAddr2, cons2, false);
                        unimportant2 = false;
                    }
                    catch
                    {
                        // ignored
                    }
                }
                // Are we comparing a field/property/local with a constant and if so then try change the type of the constant to match the type so it outputs better pseudo code and il
                // Feel free to clean it up if you feel the need to :P
                if (ArgumentTwo is ConstantDefinition constantDefinition && typeof(IConvertible).IsAssignableFrom(constantDefinition.Type) && constantDefinition.Type != typeof(string))
                {
                    TypeReference? argumentOneType = null;
                    if (ArgumentOne is ComparisonDirectPropertyAccess comparisonDirectPropertyAccess)
                    {
                        argumentOneType = comparisonDirectPropertyAccess.propertyAccessed.PropertyType;
                    } 
                    else if (ArgumentOne is ComparisonDirectFieldAccess comparisonDirectFieldAccess)
                    {
                        argumentOneType = comparisonDirectFieldAccess.fieldAccessed.FieldType;
                    }
                    else if (ArgumentOne is LocalDefinition localDefinition)
                    {
                        argumentOneType = localDefinition.Type; 
                    }
                    if (!string.IsNullOrEmpty(argumentOneType?.FullName) && !argumentOneType!.IsArray)
                    {
                        var argumentOneTypeDefinition = argumentOneType.Resolve();
                        if (argumentOneTypeDefinition?.IsEnum == true)
                        {
                            var underLyingType = typeof(int).Module.GetType(argumentOneTypeDefinition.GetEnumUnderlyingType().FullName);
                            constantDefinition.Type = underLyingType;
                            constantDefinition.Value = MiscUtils.ReinterpretBytes((IConvertible) constantDefinition.Value, underLyingType);
                        }
                        else
                        {
                            var argumentOneSystemType = typeof(int).Module.GetType(argumentOneType.FullName);
                            if (argumentOneSystemType != null && MiscUtils.TryLookupTypeDefKnownNotGeneric("System.IConvertible")!.IsAssignableFrom(argumentOneType) && argumentOneType.Name != "String")
                            {
                                constantDefinition.Value = MiscUtils.ReinterpretBytes((IConvertible) constantDefinition.Value, argumentOneType);
                                constantDefinition.Type = argumentOneSystemType;
                            }
                        }
                    }
                }
            }
            UnimportantComparison = unimportant1 || unimportant2;

            if (context.GetEndOfLoopWhichPossiblyStartsHere(associatedInstruction.GetInstructionAddress()) is { } endOfLoop && endOfLoop != 0)
            {
                EndOfLoopAddr = endOfLoop;
                context.RegisterLastInstructionOfLoopAt(this, endOfLoop);
            }

            if (ArgumentOne is LocalDefinition l1)
                RegisterUsedLocal(l1, context);

            if (ArgumentTwo is LocalDefinition l2)
                RegisterUsedLocal(l2, context);
        }
        
        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
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

            if (display2 == null || display1 == display2)
                return UnimportantComparison ? $"Compares {display1} against itself" : $"[!] Compares {display1} against itself";

                //Only show the important [!] if this is an important comparison (i.e. not an il2cpp one)
            return UnimportantComparison ? $"Compares {display1} and {display2}" : $"[!] Compares {display1} and {display2}";
        }
        
        internal bool IsProbablyWhileLoop() => EndOfLoopAddr != 0;
        
        public bool IsEitherArgument(IComparisonArgument c) => c == ArgumentOne || c == ArgumentTwo;
        
        public IComparisonArgument? GetArgumentAssociatedWithRegister(string regName)
        {
            return ArgumentOneRegister == regName ? ArgumentOne : ArgumentTwoRegister == regName ? ArgumentTwo : null;
        }

        protected abstract bool IsMemoryReferenceAnAbsolutePointer(T instruction, int operandIdx);

        protected abstract string GetRegisterName(T instruction, int opIdx);

        protected abstract string GetMemoryBaseName(T instruction);

        protected abstract ulong GetInstructionMemoryOffset(T instruction);

        protected abstract ulong GetImmediateValue(T instruction, int operandIdx);

        protected abstract ComparisonOperandType GetOperandType(T instruction, int operandIdx);

        protected abstract ulong GetMemoryPointer(T instruction, int operandIdx);

        protected IComparisonArgument? ExtractArgument(MethodAnalysis<T> context, T instruction, int operandIdx, out bool unimportant, out string? argumentRegister)
        {
            var opKind = GetOperandType(instruction, operandIdx);
            var instructionMemoryOffset = GetInstructionMemoryOffset(instruction);
            var immValue = GetImmediateValue(instruction, operandIdx);
            string registerName = GetRegisterName(instruction, operandIdx);
            
            unimportant = false;
            argumentRegister = null;

            switch (opKind)
            {
                case ComparisonOperandType.REGISTER_CONTENT:
                {
                    //Some sort of simple register operand.
                    argumentRegister = registerName;
                    var op = context.GetOperandInRegister(registerName);

                    if (op is ConstantDefinition { Value: MethodDefinition or UnknownGlobalAddr or Il2CppString _ })
                        //Ignore comparisons with looked-up method defs or unknown globals.
                        unimportant = true;

                    return op;
                }
                //An immediate constant
                case ComparisonOperandType.IMMEDIATE_CONSTANT:
                    return context.MakeConstant(typeof(ulong), immValue);
                case ComparisonOperandType.MEMORY_ADDRESS_OR_OFFSET:
                    //Otherwise, memory of some sort.

                    if (!IsMemoryReferenceAnAbsolutePointer(instruction, operandIdx))
                    {
                        //Non-absolute memory pointer - offset on a register
                        //Field/property/etc read.
                        var name = GetMemoryBaseName(instruction);

                        if (context.GetLocalInReg(name) is { } local)
                        {
                            //We know what the local is
                            if (local.Type?.IsArray == false)
                            {
                                //It's not an array
                                if (local.Type?.Resolve() == null) return null;

                                if (instructionMemoryOffset == 0)
                                {
                                    //Class pointer
                                    argumentRegister = name;
                                    var klassPtr = new Il2CppClassIdentifier
                                    {
                                        backingType = local.Type.Resolve().AsUnmanaged(),
                                        objectAlias = local.Name,
                                    };

                                    return context.MakeConstant(typeof(Il2CppClassIdentifier), klassPtr);
                                }

                                //Find a field we're accessing
                                var fields = SharedState.FieldsByType[local.Type.Resolve()];
                                var fieldName = fields.FirstOrDefault(f => f.Offset == instructionMemoryOffset).Name;

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

                            //It IS an array
                            if (Il2CppArrayUtils.IsIl2cppLengthAccessor((uint)instructionMemoryOffset))
                            {
                                //It's the length of the array
                                argumentRegister = name;
                                return new ComparisonDirectPropertyAccess
                                {
                                    localAccessedOn = local,
                                    propertyAccessed = Il2CppArrayUtils.GetLengthProperty()
                                };
                            }

                            //It's an unknown field on an array structure - or an unknown type - look up the name of the field if we can.
                            var nameOfField = Il2CppArrayUtils.GetOffsetName((uint)instructionMemoryOffset);
                            argumentRegister = name;
                            return context.MakeConstant(typeof(string), $"{{il2cpp array field {local.Name}->{nameOfField}}}");
                        }

                        //We don't know of a local in this register

                        if (context.GetConstantInReg(name) is not { } constant)
                            return null; //Unknown operand - memory type, not a global, but not a constant, or local

                        //But we do know of a constant

                        var defaultLabel = $"{{il2cpp field on {constant}, offset 0x{instructionMemoryOffset:X}}}";
                        if (constant.Type == typeof(TypeDefinition) || constant.Type == typeof(TypeReference))
                        {
                            //It's a type definition
                            unimportant = true;
                            var label = Il2CppClassUsefulOffsets.GetOffsetName((uint)instructionMemoryOffset);

                            label = label == null ? defaultLabel : $"{{il2cpp field {constant.Value}->{label}}}";
                            argumentRegister = name;
                            return context.MakeConstant(typeof(string), label);
                        }

                        //We don't know what it is.
                        unimportant = true;
                        return context.MakeConstant(typeof(string), defaultLabel);
                    }

                    //This operand is a pointer to a location in memory

                    var ptr = GetMemoryPointer(instruction, operandIdx);
                    if (LibCpp2IlMain.GetAnyGlobalByAddress(ptr) is { } usage)
                        //Specifically, a metadata usage
                        return context.MakeConstant(typeof(MetadataUsage), usage);

                    //An unknown global address
                    unimportant = true;
                    return context.MakeConstant(typeof(UnknownGlobalAddr), new UnknownGlobalAddr(ptr));
            }

            return null;
        }

        protected enum ComparisonOperandType
        {
            REGISTER_CONTENT,
            IMMEDIATE_CONSTANT,
            MEMORY_ADDRESS_OR_OFFSET,
        }
    }
}