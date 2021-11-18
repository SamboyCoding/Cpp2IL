﻿using System;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.ARM64;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis
{
    public partial class AsmAnalyzerArmV8A
    {
        protected override void PerformInstructionChecks(Arm64Instruction instruction)
        {
            if(instruction.IsSkippedData)
                return;
            
            switch (instruction.Details.Operands.Length)
            {
                case 0:
                    CheckForZeroOpInstruction(instruction);
                    break;
                case 1:
                    CheckForSingleOpInstruction(instruction);
                    break;
                case 2:
                    CheckForTwoOpInstruction(instruction);
                    break;
                case 3:
                    CheckForThreeOpInstruction(instruction);
                    break;
            }
        }

        private void CheckForZeroOpInstruction(Arm64Instruction instruction)
        {
            var mnemonic = instruction.Mnemonic;

            switch (mnemonic)
            {
                case "ret":
                    Analysis.Actions.Add(new Arm64ReturnAction(Analysis, instruction));
                    break;
            }
        }

        private void CheckForSingleOpInstruction(Arm64Instruction instruction)
        {
            var op0 = instruction.Details.Operands[0]!;
            var t0 = op0.Type;
            var r0 = op0.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;
            var r0Name = Arm64Utils.GetRegisterNameNew(r0);
            var var0 = Analysis.GetOperandInRegister(r0Name);
            var imm0 = op0.ImmediateSafe();

            var memoryBase = instruction.MemoryBase();
            var memoryOffset = instruction.MemoryOffset();
            var memoryIndex = instruction.MemoryIndex();

            switch (instruction.Mnemonic)
            {
                case "b":
                case "bl":
                    //Branch(-Link). Analogous to a JMP(/CALL) in x86.
                    var jumpTarget = (ulong)imm0;
                    if (SharedState.MethodsByAddress.TryGetValue(jumpTarget, out var managedFunctionBeingCalled))
                    {
                        Analysis.Actions.Add(new Arm64ManagedFunctionCallAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_object_new || jumpTarget == _keyFunctionAddresses.il2cpp_vm_object_new || jumpTarget == _keyFunctionAddresses.il2cpp_codegen_object_new)
                    {
                        Analysis.Actions.Add(new Arm64NewObjectAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_string_new || jumpTarget == _keyFunctionAddresses.il2cpp_vm_string_new || jumpTarget == _keyFunctionAddresses.il2cpp_string_new_wrapper 
                             || jumpTarget == _keyFunctionAddresses.il2cpp_codegen_string_new_wrapper || jumpTarget == _keyFunctionAddresses.il2cpp_vm_string_newWrapper)
                    {
                        //Creates a mono string from a string global
                        Analysis.Actions.Add(new Arm64UnmanagedToManagedStringAction(Analysis, instruction));
                    }
                    else if (LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.ContainsKey(jumpTarget))
                    {
                        //Call concrete generic function
                        Analysis.Actions.Add(new Arm64ManagedFunctionCallAction(Analysis, instruction));
                    }
                    else if (jumpTarget < MiscUtils.GetAddressOfNextFunctionStart((ulong)instruction.Address) && jumpTarget > (ulong)instruction.Address)
                    {
                        //Jumping over an instruction, may need to expand function to include jumpTarget.
                    }
                    else if (Arm64CallThrowHelperAction.IsThrowHelper((long)jumpTarget))
                    {
                        Analysis.Actions.Add(new Arm64CallThrowHelperAction(Analysis, instruction));
                        break; //Skip adding a return lower down.
                    }

                    //If we're a b, we need a return too
                    if (instruction.Mnemonic == "b")
                        Analysis.Actions.Add(new Arm64ReturnAction(Analysis, instruction));
                    break;
                case "br":
                case "blr":
                    //Branch to register

                    //This part is TODO
                    //because we need to know what's in the register first (e.g. virtual function)

                    //We need a ret if br
                    if(instruction.Mnemonic == "br")
                        Analysis.Actions.Add(new Arm64ReturnAction(Analysis, instruction));
                    break;
            }
        }

        private void CheckForTwoOpInstruction(Arm64Instruction instruction)
        {
            var op0 = instruction.Details.Operands[0]!;
            var op1 = instruction.Details.Operands[1]!;
            var offset0 = instruction.MemoryOffset();
            var offset1 = offset0; // TODO?

            var t0 = op0.Type;
            var t1 = op1.Type;

            var r0 = op0.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;
            var r1 = op1.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;

            var r0Name = Arm64Utils.GetRegisterNameNew(r0);
            var r1Name = Arm64Utils.GetRegisterNameNew(r1);

            var var0 = Analysis.GetOperandInRegister(r0Name);
            var var1 = Analysis.GetOperandInRegister(r1Name);

            var imm0 = op0.ImmediateSafe();
            var imm1 = op1.ImmediateSafe();

            var memoryBase = instruction.MemoryBase()?.Id ?? Arm64RegisterId.Invalid;
            var memoryOffset = instruction.MemoryOffset();
            var memoryIndex = instruction.MemoryIndex()?.Id ?? Arm64RegisterId.Invalid;

            var memVar = Analysis.GetOperandInRegister(Arm64Utils.GetRegisterNameNew(memoryBase));

            var mnemonic = instruction.Mnemonic;
            if (mnemonic is "ldrb" or "ldrh")
                mnemonic = "ldr";
            if (mnemonic is "strb" or "strh")
                mnemonic = "str";
            if (mnemonic is "movz")
                mnemonic = "mov";

            //The single most annoying part about capstone is that its mnemonics are strings.
            switch (mnemonic)
            {
                case "adrp":
                    //Load address to register.
                    //Does not READ the address, only copies that number, essentially.
                    Analysis.Actions.Add(new Arm64AddressToRegisterAction(Analysis, instruction));
                    break;
                case "cbnz":
                    //Compare and branch if non-0
                    //Again, skip the second op in the comparison, because it's the address to jump to.
                    Analysis.Actions.Add(new Arm64ComparisonAction(Analysis, instruction, true));
                    Analysis.Actions.Add(new Arm64JumpIfNonZeroOrNonNullAction(Analysis, instruction, 1));
                    break;
                case "cbz":
                    //Compare *and* branch if 0
                    //But, skip the second op in the comparison, because it's the address to jump to.
                    Analysis.Actions.Add(new Arm64ComparisonAction(Analysis, instruction, true));
                    Analysis.Actions.Add(new Arm64JumpIfZeroOrNullAction(Analysis, instruction, 1));
                    break;
                case "cmp":
                    Analysis.Actions.Add(new Arm64ComparisonAction(Analysis, instruction));
                    break;
                case "ldr" when t1 == Arm64OperandType.Memory && t0 == Arm64OperandType.Register && memVar is LocalDefinition potentialDummyAttributeList && potentialDummyAttributeList.Type?.Resolve() == AttributeRestorer.DummyTypeDefForAttributeList:
                    //Load attribute from attribute list.
                    Analysis.Actions.Add(new Arm64LoadAttributeFromAttributeListAction(Analysis, instruction, AttributesForRestoration));
                    break;
                case "ldr" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Memory && memVar is LocalDefinition && memoryOffset != 0:
                    //Field read - non-zero memory offset on local to register.
                    Analysis.Actions.Add(new Arm64FieldReadToRegAction(Analysis, instruction));
                    break;
                case "ldr" when t1 == Arm64OperandType.Memory && (offset1 == 0 || r0 == Arm64RegisterId.ARM64_REG_SP) && offset1 == 0 && memVar is LocalDefinition && memoryIndex == Arm64RegisterId.Invalid:
                {
                    //Zero offsets, but second operand is a memory pointer -> class pointer move.
                    //MUST Check for non-cpp type
                    if (Analysis.GetLocalInReg(Arm64Utils.GetRegisterNameNew(memoryBase)) != null)
                    {
                        Analysis.Actions.Add(new Arm64ClassPointerLoadAction(Analysis, instruction)); //We have a managed local type, we can load the class pointer for it
                    }
                    return;
                }
                case "ldr" when t1 == Arm64OperandType.Memory && t0 == Arm64OperandType.Register && memoryIndex == Arm64RegisterId.Invalid && memVar is ConstantDefinition constant && constant.Type == typeof(StaticFieldsPtr):
                    //Load a specific static field.
                    Analysis.Actions.Add(new Arm64StaticFieldToRegAction(Analysis, instruction));
                    break;
                case "ldr" when t1 == Arm64OperandType.Memory && t0 == Arm64OperandType.Register && memoryIndex == Arm64RegisterId.Invalid && memVar is ConstantDefinition { Value: TypeReference _ } && Il2CppClassUsefulOffsets.IsStaticFieldsPtr((uint)offset1):
                    //Static fields ptr read
                    Analysis.Actions.Add(new Arm64StaticFieldOffsetToRegAction(Analysis, instruction));
                    break;
                case "ldr" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Memory && memVar is ConstantDefinition && memoryOffset == 0:
                    //Dereferencing a pointer to a metadata usage
                    Analysis.Actions.Add(new Arm64DereferencePointerAction(Analysis, instruction));
                    break;
                case "ldr" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Memory && memVar is ConstantDefinition { Value: long pageAddress } && memoryOffset < 0x4000:
                    //Combined with adrp to load a global. The adrp loads the page, and this adds an additional offset to resolve a specific memory value.
                    var globalAddress = (ulong)(pageAddress + memoryOffset);
                    MetadataUsage global = null;
                    if (LibCpp2IlMain.GetAnyGlobalByAddress(globalAddress) is { IsValid: true } global2)
                        global = global2;
                    else
                    {
                        //Try pointer to global
                        try
                        {
                            var possiblePtr = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<ulong>(globalAddress);
                            if (LibCpp2IlMain.GetAnyGlobalByAddress(possiblePtr) is { IsValid: true } global3)
                                global = global3;
                        }
                        catch (Exception)
                        {
                            //Nothing
                        }
                    }

                    if (global != null)
                    {
                        //Have a global here.
                        switch (global.Type)
                        {
                            case MetadataUsageType.Type:
                            case MetadataUsageType.TypeInfo:
                                Analysis.Actions.Add(new Arm64MetadataUsageTypeToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.MethodDef:
                                Analysis.Actions.Add(new Arm64MetadataUsageMethodDefToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.MethodRef:
                                Analysis.Actions.Add(new Arm64MetadataUsageMethodRefToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.FieldInfo:
                                Analysis.Actions.Add(new Arm64MetadataUsageFieldToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.StringLiteral:
                                Analysis.Actions.Add(new Arm64MetadataUsageLiteralToRegisterAction(Analysis, instruction));
                                break;
                        }

                        return;
                    }
                    
                    if(!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(globalAddress, out var rawAddr))
                        //Possibly BSS? (all-zero segment for static uninitialized memory, not present in the file)
                        //Either way, bail out
                        break;

                    //Unknown global or string
                    var potentialLiteral = MiscUtils.TryGetLiteralAt(LibCpp2IlMain.Binary!, (ulong) rawAddr);
                    if (potentialLiteral != null && instruction.Details.Operands[0].RegisterSafe()?.Name[0] != 'v')
                    {
                        Analysis.Actions.Add(new Arm64UnmanagedLiteralToConstantAction(Analysis, instruction, potentialLiteral, globalAddress));
                    }
                    else
                    {
                        //Unknown global
                        Analysis.Actions.Add(new Arm64UnknownGlobalToConstantAction(Analysis, instruction, globalAddress));
                    }

                    break;
                case "mov" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Register && r1Name == "xzr":
                    //Move zero register to other register
                    Analysis.Actions.Add(new Arm64ZeroRegisterToRegisterAction(Analysis, instruction));
                    break;
                case "mov" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Immediate:
                    //Move immediate to reg
                    var mayNotBeAConstant = MNEMONICS_INDICATING_CONSTANT_IS_NOT_CONSTANT
                        .Any(m => _instructions
                            .Any(i => !i.IsSkippedData && i.Mnemonic == m && !i.Details.Operands
                                .Any(o => Arm64Utils.GetRegisterNameNew(o.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid) is "sp")));
                    
                    Analysis.Actions.Add(new Arm64ImmediateToRegAction(Analysis, instruction, mayNotBeAConstant));
                    break;
                case "mov" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Register && var1 is { }:
                    //Move generic analyzed op to another reg
                    Analysis.Actions.Add(new Arm64RegCopyAction(Analysis, instruction));
                    break;
                case "str" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Memory && var0 is { } && memVar is LocalDefinition:
                    //Field write from register.
                    //Unlike a bunch of other instructions, source is operand 0, destination is operand 1.
                    Analysis.Actions.Add(new Arm64RegisterToFieldAction(Analysis, instruction));
                    break;
                case "str" when t0 is Arm64OperandType.Immediate && t1 is Arm64OperandType.Memory && memVar is LocalDefinition:
                    //Field write from immediate
                    Analysis.Actions.Add(new Arm64ImmediateToFieldAction(Analysis, instruction));
                    break;
                case "str" when t1 is Arm64OperandType.Memory && t0 is Arm64OperandType.Register && var0 is { } && memVar is ConstantDefinition { Value: StaticFieldsPtr _ }:
                    //Static Field write from register.
                    Analysis.Actions.Add(new Arm64RegisterToStaticFieldAction(Analysis, instruction));
                    break;
            }
        }

        private void CheckForThreeOpInstruction(Arm64Instruction instruction)
        {
            var op0 = instruction.Details.Operands[0]!;
            var op1 = instruction.Details.Operands[1]!;
            var op2 = instruction.Details.Operands[2]!;

            var t0 = op0.Type;
            var t1 = op1.Type;
            var t2 = op2.Type;

            var r0 = op0.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;
            var r1 = op1.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;
            var r2 = op2.RegisterSafe()?.Id ?? Arm64RegisterId.Invalid;

            var r0Name = Arm64Utils.GetRegisterNameNew(r0);
            var r1Name = Arm64Utils.GetRegisterNameNew(r1);
            var r2Name = Arm64Utils.GetRegisterNameNew(r2);

            var var0 = Analysis.GetOperandInRegister(r0Name);
            var var1 = Analysis.GetOperandInRegister(r1Name);
            var var2 = Analysis.GetOperandInRegister(r2Name);

            var imm0 = op0.ImmediateSafe();
            var imm1 = op1.ImmediateSafe();
            var imm2 = op2.ImmediateSafe();

            var memoryBase = instruction.MemoryBase()?.Id ?? Arm64RegisterId.Invalid;
            var memoryOffset = instruction.MemoryOffset();
            var memoryIndex = instruction.MemoryIndex()?.Id ?? Arm64RegisterId.Invalid;

            var memoryBaseName = Arm64Utils.GetRegisterNameNew(memoryBase);
            var memVar = Analysis.GetOperandInRegister(memoryBaseName);

            var mnemonic = instruction.Mnemonic;

            switch (mnemonic)
            {
                case "ldp" when memVar is LocalDefinition && t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Register && t2 is Arm64OperandType.Memory:
                {
                    //ldp reg1, reg2, [memReg, offset]
                    //Load field from memReg at offset into reg1, and another at offset + sizeof(reg1) into reg2.
                    Analysis.Actions.Add(new Arm64FieldReadToRegAction(Analysis, instruction, memoryBaseName, (ulong) memoryOffset, r0Name));
                    //Bit more complicated, we have to work out size of reg 1
                    var size = 4;
                    if (r0Name[0] is 'x')
                        size = 8;

                    Analysis.Actions.Add(new Arm64FieldReadToRegAction(Analysis, instruction, memoryBaseName, (ulong) (memoryOffset + size), r1Name));
                    break;
                }
                case "orr" when r1Name is "xzr" && t2 == Arm64OperandType.Immediate && imm2 != 0:
                    //ORR dest, xzr, #n
                    //dest = n, basically. Technically 0 | n, but that's the same.
                    Analysis.Actions.Add(new Arm64OrZeroAndImmAction(Analysis, instruction));
                    break;
                case "add" when t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Register && t2 == Arm64OperandType.Immediate && var0 is ConstantDefinition { Value: long pageAddress } && imm2 < 0x4000:
                    //Combined with adrp to load a global. The adrp loads the page, and this adds an additional offset to resolve a specific memory value.
                    var globalAddress = (ulong)(pageAddress + imm2);
                    MetadataUsage global = null;
                    if (LibCpp2IlMain.GetAnyGlobalByAddress(globalAddress) is { IsValid: true } global2)
                        global = global2;
                    else
                    {
                        //Try pointer to global
                        try
                        {
                            var possiblePtr = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<ulong>(globalAddress);
                            if (LibCpp2IlMain.GetAnyGlobalByAddress(possiblePtr) is { IsValid: true } global3)
                                global = global3;
                        }
                        catch (Exception)
                        {
                            //Nothing
                        }
                    }

                    if (global != null)
                    {
                        //Have a global here.
                        switch (global.Type)
                        {
                            case MetadataUsageType.Type:
                            case MetadataUsageType.TypeInfo:
                                Analysis.Actions.Add(new Arm64MetadataUsageTypeToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.MethodDef:
                                Analysis.Actions.Add(new Arm64MetadataUsageMethodDefToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.MethodRef:
                                Analysis.Actions.Add(new Arm64MetadataUsageMethodRefToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.FieldInfo:
                                Analysis.Actions.Add(new Arm64MetadataUsageFieldToRegisterAction(Analysis, instruction));
                                break;
                            case MetadataUsageType.StringLiteral:
                                Analysis.Actions.Add(new Arm64MetadataUsageLiteralToRegisterAction(Analysis, instruction));
                                break;
                        }

                        return;
                    }
                    
                    if(!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(globalAddress, out var rawAddr))
                        //Possibly BSS, bail out
                        break;


                    //Unknown global or string
                    var potentialLiteral = MiscUtils.TryGetLiteralAt(LibCpp2IlMain.Binary!, (ulong)rawAddr);
                    if (potentialLiteral != null && instruction.Details.Operands[0].RegisterSafe()?.Name[0] != 'v')
                    {
                        Analysis.Actions.Add(new Arm64UnmanagedLiteralToConstantAction(Analysis, instruction, potentialLiteral, globalAddress));
                    }
                    else
                    {
                        //Unknown global
                        Analysis.Actions.Add(new Arm64UnknownGlobalToConstantAction(Analysis, instruction, globalAddress));
                    }

                    break;
                case "stp" when memVar is LocalDefinition && t0 is Arm64OperandType.Register && t1 is Arm64OperandType.Register && t2 is Arm64OperandType.Memory:
                {
                    //stp reg1, reg2, [memReg, offset]
                    //Store value from reg1 into field at offset into reg1, and another into offset + sizeof(reg1) from reg2.
                    Analysis.Actions.Add(new Arm64RegisterToFieldAction(Analysis, instruction, memoryBaseName, r0Name, (ulong) memoryOffset));
                    
                    //Work out size of reg 1. More than this may be needed?
                    var size = 4;
                    if (r0Name[0] is 'x')
                        size = 8;

                    Analysis.Actions.Add(new Arm64RegisterToFieldAction(Analysis, instruction, memoryBaseName, r1Name, (ulong) (memoryOffset + size)));
                    break;
                }
            }
        }
    }
}