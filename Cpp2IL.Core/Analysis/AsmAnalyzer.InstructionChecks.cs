using System.Diagnostics;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions;
using Cpp2IL.Core.Analysis.Actions.Important;
using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis
{
    internal partial class AsmAnalyzer
    {
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
            if (instruction.IP == 0x1804ae0ac)
                Debugger.Break();

            var reg = Utils.GetRegisterNameNew(instruction.Op0Register == Register.None ? instruction.MemoryBase : instruction.Op0Register);
            var operand = Analysis.GetOperandInRegister(reg);

            var memR = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var memOp = Analysis.GetOperandInRegister(memR);
            var memOffset = instruction.MemoryDisplacement64;

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
                case Mnemonic.Push when _cppAssembly.is32Bit && instruction.Op0Kind == OpKind.Memory && memOffset != 0 && instruction.MemoryBase == Register.None:
                case Mnemonic.Push when _cppAssembly.is32Bit && instruction.Op0Kind.IsImmediate() && LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(instruction.Immediate32, out _):
                {
                    //Global to stack or reg. Could be metadata literal, non-metadata literal, metadata type, or metadata method.
                    var globalAddress = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : memOffset;
                    if (LibCpp2IlMain.GetAnyGlobalByAddress(globalAddress) is { } global)
                    {
                        Analysis.Actions.Add(new PushGlobalAction(Analysis, instruction));
                        break;
                    }

                    var potentialLiteral = Utils.TryGetLiteralAt(LibCpp2IlMain.Binary!, (ulong) LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(globalAddress));
                    if (potentialLiteral != null && !instruction.Op0Register.IsXMM())
                    {
                        // if (reg != "rsp")
                        Analysis.Actions.Add(new Il2CppStringToConstantAction(Analysis, instruction, potentialLiteral));

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

                    break;
                }
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
                case Mnemonic.Call when instruction.Op0Kind == OpKind.Register && operand is ConstantDefinition {Value: MethodReference _}:
                case Mnemonic.Call when instruction.Op0Kind == OpKind.Memory && memOp is ConstantDefinition {Value: MethodReference _}:
                case Mnemonic.Call when instruction.Op0Kind == OpKind.Register && operand is ConstantDefinition {Value: GenericMethodReference _}:
                case Mnemonic.Call when instruction.Op0Kind == OpKind.Memory && memOp is ConstantDefinition {Value: GenericMethodReference _}:
                    Analysis.Actions.Add(new CallManagedFunctionInRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Jmp when instruction.Op0Kind == OpKind.Memory && memOffset == 0 && operand is ConstantDefinition {Value: MethodReference _}:
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
                    else if (LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.ContainsKey(jumpTarget))
                    {
                        //Call concrete generic function
                        Analysis.Actions.Add(new CallManagedFunctionAction(Analysis, instruction));
                        Analysis.Actions.Add(new ReturnFromFunctionAction(Analysis, instruction)); //Implcit return
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
                case Mnemonic.Call when instruction.Op0Kind == OpKind.Memory && memOffset > 0x128 && operand is ConstantDefinition {Value: Il2CppClassIdentifier _}:
                    //Virtual method call
                    Analysis.Actions.Add(new CallVirtualMethodAction(Analysis, instruction));
                    break;
                case Mnemonic.Call when instruction.Op0Kind == OpKind.Memory && operand is ConstantDefinition {Value: Il2CppMethodSpec _}:
                    //Call method spec (used in RGCTX)
                    Analysis.Actions.Add(new CallMethodSpecAction(Analysis, instruction));
                    break;
                case Mnemonic.Call when instruction.Op0Kind != OpKind.Register && instruction.Op0Kind != OpKind.Memory:
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    if (SharedState.MethodsByAddress.ContainsKey(jumpTarget))
                    {
                        //Call a managed function
                        Analysis.Actions.Add(new CallManagedFunctionAction(Analysis, instruction));
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
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_codegen_object_new || jumpTarget == _keyFunctionAddresses.il2cpp_vm_object_new || jumpTarget == _keyFunctionAddresses.il2cpp_object_new)
                    {
                        //Allocate object
                        Analysis.Actions.Add(new AllocateInstanceAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_array_new_specific || jumpTarget == _keyFunctionAddresses.SzArrayNew || jumpTarget == _keyFunctionAddresses.il2cpp_vm_array_new_specific)
                    {
                        //Allocate array
                        Analysis.Actions.Add(new AllocateArrayAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_resolve_icall || jumpTarget == _keyFunctionAddresses.InternalCalls_Resolve)
                    {
                        //Resolve ICall
                        Analysis.Actions.Add(new LookupICallAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_value_box || jumpTarget == _keyFunctionAddresses.il2cpp_vm_object_box)
                    {
                        //Box a cpp primitive to a managed type
                        Analysis.Actions.Add(new BoxValueAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_object_is_inst)
                    {
                        //Safe cast an object to a type
                        Analysis.Actions.Add(new SafeCastAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_vm_exception_raise || jumpTarget == _keyFunctionAddresses.il2cpp_raise_exception || jumpTarget == _keyFunctionAddresses.il2cpp_codegen_raise_exception)
                    {
                        //Equivalent of a throw statement
                        Analysis.Actions.Add(new ThrowAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.AddrPInvokeLookup)
                    {
                        //P/Invoke lookup
                        //TODO work out how this works
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_type_get_object || jumpTarget == _keyFunctionAddresses.il2cpp_vm_reflection_get_type_object)
                    {
                        //Equivalent to typeof(blah)
                        Analysis.Actions.Add(new TypeToObjectAction(Analysis, instruction));
                    }
                    else if (jumpTarget == _keyFunctionAddresses.il2cpp_string_new || jumpTarget == _keyFunctionAddresses.il2cpp_vm_string_new)
                    {
                        //Creates a mono string from a string global
                        Analysis.Actions.Add(new GlobalStringToMonoStringAction(Analysis, instruction));
                    }
                    else if (CallExceptionThrowerFunction.IsExceptionThrower(jumpTarget))
                    {
                        Analysis.Actions.Add(new CallExceptionThrowerFunction(Analysis, instruction));
                    }
                    else if (LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.ContainsKey(jumpTarget))
                    {
                        //Call concrete generic function
                        Analysis.Actions.Add(new CallManagedFunctionAction(Analysis, instruction));
                    }
                    else if (Analysis.GetLocalInReg("rcx") is { }
                             && Analysis.GetConstantInReg("rdx") is {Value: TypeDefinition _}
                             && (Analysis.GetConstantInReg("r8") is {Value: uint _} || Analysis.GetLocalInReg("r8") is {Type: {Name: "UInt32"}})
                             && Analysis.Actions.Any(a => a is LocateSpecificInterfaceOffsetAction)
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
                case Mnemonic.Imul when instruction.Op0Kind == OpKind.Register:
                    //IMUL reg
                    Analysis.Actions.Add(new SingleOperandImulAction(Analysis, instruction));
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

            var offset0 = instruction.MemoryDisplacement32;
            var offset1 = offset0; //todo?

            var type0 = instruction.Op0Kind;
            var type1 = instruction.Op1Kind;

            var mnemonic = instruction.Mnemonic;

            if (mnemonic == Mnemonic.Movzx || mnemonic == Mnemonic.Movss || mnemonic == Mnemonic.Movsxd || mnemonic == Mnemonic.Movaps || mnemonic == Mnemonic.Movups || mnemonic == Mnemonic.Movdqa)
                mnemonic = Mnemonic.Mov;

            if (mnemonic == Mnemonic.Comiss)
                mnemonic = Mnemonic.Cmp;

            //Noting here, format of a memory operand is:
            //[memoryBase + memoryIndex * memoryIndexScale + memoryOffset]

            switch (mnemonic)
            {
                case Mnemonic.Mov when !_cppAssembly.is32Bit && type0 == OpKind.Register && type1 == OpKind.Register && offset0 == 0 && offset1 == 0 && op1 != null && instruction.Op1Register.IsGPR32():
                    //Move of a 32-bit register when we are 64-bit - check for structs
                    if (op1 is LocalDefinition {Type: {IsValueType: true, IsPrimitive: false} t})
                    {
                        //Struct.
                        if (FieldUtils.GetFieldBeingAccessed(t!, 0, false) != FieldUtils.GetFieldBeingAccessed(t!, 4, false))
                        {
                            //Different fields at 0 and 4 (i.e. first field is an int, etc).
                            Analysis.Actions.Add(new Implicit4ByteFieldReadAction(Analysis, instruction));
                            break;
                        }
                    }

                    //Fallback to a reg->reg move.
                    Analysis.Actions.Add(new RegToRegMoveAction(Analysis, instruction));
                    break;
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
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memOp is LocalDefinition l && l.Type == AttributeRestorer.DummyTypeDefForAttributeList:
                    //Move of attribute list entry, used for restoration of attribute constructors
                    var ptrSize = LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
                    var offsetInList = instruction.MemoryDisplacement32 / ptrSize;

                    if (offsetInList < AttributesForRestoration!.Count)
                        Analysis.Actions.Add(new LoadAttributeFromAttributeListAction(Analysis, instruction, AttributesForRestoration!));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && (offset0 == 0 || r0 == "rsp") && offset1 == 0 && memOp is LocalDefinition && instruction.MemoryIndex == Register.None:
                {
                    //Zero offsets, but second operand is a memory pointer -> class pointer move.
                    //MUST Check for non-cpp type
                    if (Analysis.GetLocalInReg(memR) != null)
                        Analysis.Actions.Add(new ClassPointerLoadAction(Analysis, instruction)); //We have a managed local type, we can load the class pointer for it
                    return;
                }
                case Mnemonic.Lea when !_cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryBase == Register.RIP && instruction.MemoryIndex == Register.None:
                case Mnemonic.Mov when !_cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryBase == Register.RIP && instruction.MemoryIndex == Register.None:
                case Mnemonic.Lea when _cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryBase == Register.None && instruction.MemoryDisplacement64 != 0 && instruction.MemoryIndex == Register.None:
                case Mnemonic.Mov when _cppAssembly.is32Bit && type1 == OpKind.Memory && instruction.MemoryBase == Register.None && instruction.MemoryDisplacement64 != 0 && instruction.MemoryIndex == Register.None:
                {
                    //Global to stack or reg. Could be metadata literal, non-metadata literal, metadata type, or metadata method.
                    var globalAddress = instruction.MemoryDisplacement64;
                    if (LibCpp2IlMain.GetAnyGlobalByAddress(globalAddress) is { IsValid: true } global)
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
                        var potentialLiteral = Utils.TryGetLiteralAt(LibCpp2IlMain.Binary!, (ulong) LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(instruction.GetRipBasedInstructionMemoryAddress()));
                        if (potentialLiteral != null && !instruction.Op0Register.IsXMM())
                        {
                            if (r0 != "rsp")
                                Analysis.Actions.Add(new Il2CppStringToConstantAction(Analysis, instruction, potentialLiteral));

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
                case Mnemonic.Lea when type0 == OpKind.Register && type1 == OpKind.Memory && memOp is LocalDefinition {KnownInitialValue: 0}:
                    //lea dest, [knownZero+amount] => load constant of [amount] into [dest]
                    Analysis.Actions.Add(new LoadConstantUsingLeaAction(Analysis, instruction));
                    break;
                case Mnemonic.Lea when type0 == OpKind.Register && type1 == OpKind.Memory && memR == "rsp":
                    //LEA dest, [rsp+0xblah] => load stack pointer
                    Analysis.Actions.Add(new StackPointerToRegisterAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1.IsImmediate() && type0 == OpKind.Register:
                    //Constant move to reg
                    var mayNotBeAConstant = MNEMONICS_INDICATING_CONSTANT_IS_NOT_CONSTANT.Any(m => _instructions.Any(i => i.Mnemonic == m && Utils.GetRegisterNameNew(i.Op0Register) != "rsp"));

                    Analysis.Actions.Add(new ConstantToRegAction(Analysis, instruction, mayNotBeAConstant));
                    return;
                case Mnemonic.Mov when type1 >= OpKind.Immediate8 && type1 <= OpKind.Immediate32to64 && offset0 != 0 && type0 == OpKind.Memory && instruction.MemoryBase == Register.None:
                    //Move constant to addr
                    Analysis.Actions.Add(new ConstantToGlobalAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1.IsImmediate() && offset0 != 0 && type0 != OpKind.Register && memR != "rip" && memR != "rbp" && memR != "rsp":
                    if (memOp is LocalDefinition {KnownInitialValue: AllocatedArray _})
                    {
                        if (Il2CppArrayUtils.IsAtLeastFirstItemPtr(instruction.MemoryDisplacement32))
                        {
                            //Array write
                            Analysis.Actions.Add(new ImmediateToArrayAction(Analysis, instruction));
                        }
                    }
                    else
                    {
                        //Immediate move to field
                        Analysis.Actions.Add(new ImmediateToFieldAction(Analysis, instruction));
                    }

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
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && Analysis.GetConstantInReg(memR) != null && Il2CppClassUsefulOffsets.IsStaticFieldsPtr(instruction.MemoryDisplacement32):
                    //Static fields ptr read
                    Analysis.Actions.Add(new StaticFieldOffsetToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is LocalDefinition {Type: {IsArray: true}}:
                    //Move reg, [reg+0x10]
                    //Reading an element from an array at a fixed offset
                    if (Il2CppArrayUtils.IsAtLeastFirstItemPtr(instruction.MemoryDisplacement32))
                    {
                        Analysis.Actions.Add(new ConstantArrayOffsetToRegAction(Analysis, instruction));
                    }
                    else if (Il2CppArrayUtils.IsIl2cppLengthAccessor(instruction.MemoryDisplacement32))
                    {
                        Analysis.Actions.Add(new ArrayLengthPropertyToLocalAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Lea when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is LocalDefinition {Type: {IsArray: true}}:
                    //Same as above but an LEA - copying a reference to the *address* of this item to the reg.
                    if (Il2CppArrayUtils.IsAtLeastFirstItemPtr(instruction.MemoryDisplacement32))
                    {
                        Analysis.Actions.Add(new ConstantArrayOffsetPointerToRegAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex != Register.None && instruction.MemoryDisplacement32 == Il2CppArrayUtils.FirstItemOffset && memOp is LocalDefinition {Type: {IsArray: true}}:
                    //Mov reg, [reg + index * value + 20h]
                    //Array read of index-th element (assuming value is equal to sizeof(elementType) )
                    Analysis.Actions.Add(new ArrayElementReadToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex != Register.None && memIdxOp is LocalDefinition {Type: {IsArray: true}}:
                    //Move reg, [reg+reg] => usually array reads.
                    //So much so that this is guarded behind an array read check - change the case if you need to change this.
                    Analysis.Actions.Add(new RegOffsetArrayValueReadRegToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when !LibCpp2IlMain.Binary!.is32Bit && type1 == OpKind.Memory && type0 == OpKind.Register && memR == "rsp" && instruction.MemoryIndex == Register.None:
                    //x64 Stack pointer read.
                    Analysis.Actions.Add(new StackOffsetReadX64Action(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && memOp is ConstantDefinition {Value: Il2CppClassIdentifier _}:
                    if (Il2CppClassUsefulOffsets.IsInterfaceOffsetsPtr(offset1))
                    {
                        //Class pointer interface offset read
                        Analysis.Actions.Add(new InterfaceOffsetsReadAction(Analysis, instruction));
                    }
                    else if (Il2CppClassUsefulOffsets.IsPointerIntoVtable(offset1))
                    {
                        //Virtual method pointer load
                        Analysis.Actions.Add(new LoadVirtualFunctionPointerAction(Analysis, instruction));
                    }
                    else if (Il2CppClassUsefulOffsets.IsInterfaceOffsetsCount(offset1))
                    {
                        //Interface offsets count
                        Analysis.Actions.Add(new InterfaceOffsetCountToLocalAction(Analysis, instruction));
                    }
                    else if (Il2CppClassUsefulOffsets.IsRGCTXDataPtr(offset1))
                    {
                        //RGCTX data read
                        Analysis.Actions.Add(new ReadRGCTXDataListAction(Analysis, instruction));
                    }
                    else if (Il2CppClassUsefulOffsets.IsElementTypePtr(offset1))
                    {
                        //Element Type Read
                        //Valid for arrays at least, maybe others
                        Analysis.Actions.Add(new ReadElementTypeFromClassPtrAction(Analysis, instruction));
                    }
                    else
                    {
                        Analysis.Actions.Add(new UnknownClassOffsetReadAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is ConstantDefinition {Value: MethodReference _}:
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is ConstantDefinition {Value: GenericMethodReference _}:
                    //Read offset on method global
                    if (Il2CppMethodDefinitionUsefulOffsets.IsSlotOffset(instruction.MemoryDisplacement32))
                    {
                        Analysis.Actions.Add(new MethodSlotToLocalAction(Analysis, instruction));
                    }

                    if (Il2CppMethodDefinitionUsefulOffsets.IsKlassPtr(instruction.MemoryDisplacement32))
                    {
                        Analysis.Actions.Add(new MethodDefiningTypeToConstantAction(Analysis, instruction));
                    }

                    if (Il2CppMethodDefinitionUsefulOffsets.IsMethodPtr(instruction.MemoryDisplacement32) || instruction.MemoryDisplacement32 == 0)
                    {
                        Analysis.Actions.Add(new MoveMethodInfoPtrToRegAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is ConstantDefinition {Value: Il2CppRGCTXArray _}:
                    //Read RGCTX array value
                    Analysis.Actions.Add(new ReadSpecificRGCTXDataAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is LocalDefinition {IsMethodInfoParam: true}:
                    //MethodInfo offset read
                    if (Il2CppMethodInfoUsefulOffsets.IsKlassPtr(instruction.MemoryDisplacement32))
                    {
                        //Read klass ptr.
                        Analysis.Actions.Add(new LoadClassPointerFromMethodInfoAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Mov when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && instruction.MemoryIndex == Register.None && memOp is LocalDefinition loc:
                    //Move generic memory to register - field read.
                    if (loc.Type == AttributeRestorer.DummyTypeDefForAttributeList)
                    {
                        //Ignore these two - they aren't real, and there was an analysis failure further up.
                        break;
                    }

                    Analysis.Actions.Add(new FieldToLocalAction(Analysis, instruction));
                    break;
                case Mnemonic.Lea when type1 == OpKind.Memory && type0 == OpKind.Register && memR != "rip" && memOp is LocalDefinition && instruction.MemoryIndex == Register.None:
                    //LEA generic memory to register - field pointer load.
                    Analysis.Actions.Add(new FieldPointerToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is ConstantDefinition {Value: StaticFieldsPtr _}:
                    //Write static field
                    Analysis.Actions.Add(new RegToStaticFieldAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is LocalDefinition {Type: ArrayType _} && Il2CppArrayUtils.IsAtLeastFirstItemPtr(instruction.MemoryDisplacement32):
                    Analysis.Actions.Add(new RegToConstantArrayOffsetAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is LocalDefinition:
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is ConstantDefinition {Value: FieldPointer _}:
                    //Write non-static field
                    Analysis.Actions.Add(new RegToFieldAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR != "rip" && memOp is ConstantDefinition {Value: Il2CppArrayOffsetPointer<Instruction> _}:
                    //Write into array offset via pointer
                    Analysis.Actions.Add(new RegisterToArrayViaPointerAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1 == OpKind.Register && memR == "rsp":
                    //Write to stack

                    if (op1 is LocalDefinition)
                        Analysis.Actions.Add(new LocalToStackOffsetAction(Analysis, instruction));
                    else if (op1 is ConstantDefinition)
                        Analysis.Actions.Add(new ConstantToStackOffsetAction(Analysis, instruction));
                    break;
                case Mnemonic.Mov when type0 == OpKind.Memory && type1.IsImmediate() && memR == "rsp":
                    Analysis.Actions.Add(new ImmediateToStackOffsetAction(Analysis, instruction));
                    break;
                //TODO Everything from CheckForFieldArrayAndStackReads
                //TODO More Arithmetic
                case Mnemonic.Add when type0 == OpKind.Register && type1.IsImmediate() && r0 != "rsp" && op0 is LocalDefinition l && !Utils.IsNumericType(l.Type):
                    //Add reg, val
                    //But value in reg is non-numeric
                    Analysis.Actions.Add(new FieldPointerToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Add when type0 == OpKind.Register && type1.IsImmediate() && r0 != "rsp":
                    //Add reg, val
                    Analysis.Actions.Add(new AddConstantToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Add when type0 == OpKind.Register && type1 == OpKind.Register && r0 != "rsp":
                    //Add reg, reg
                    Analysis.Actions.Add(new AddRegToRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Sub when type0 == OpKind.Register && type1 == OpKind.Register && r0 != "rsp":
                    //Sub reg, reg
                    Analysis.Actions.Add(new SubtractRegFromRegAction(Analysis, instruction));
                    break;
                case Mnemonic.Mulss when type0 == OpKind.Register && type1 == OpKind.Memory && memR == "rip":
                    //mulss reg, [rip+0xblah]
                    Analysis.Actions.Add(new MultiplyRegByGlobalAction(Analysis, instruction));
                    break;
                case Mnemonic.Divss when type0 == OpKind.Register && type1 == OpKind.Memory && memR == "rip":
                    //divss reg, [rip+0xblah]
                    Analysis.Actions.Add(new DivideRegByGlobalAction(Analysis, instruction));
                    break;
                case Mnemonic.Xor:
                case Mnemonic.Xorps:
                {
                    //PROBABLY clear register
                    if (r0 == r1)
                        Analysis.Actions.Add(new ClearRegAction(Analysis, instruction));
                    break;
                }
                case Mnemonic.Or when type0 == OpKind.Register && type1.IsImmediate() && instruction.GetImmediate(1) == 0xFFFF_FFFF_FFFF_FFFF:
                    //OR reg, 0xFFFFFFFF
                    //Even though this is a 32-bit immediate, Iced reports it as a 64-bit one.
                    Analysis.Actions.Add(new OrToMinusOneAction(Analysis, instruction));
                    break;
                //Check if we have an Interface Offset array in the memory operand
                case Mnemonic.Cmp when memOp is ConstantDefinition {Value: Il2CppInterfaceOffset[] _}:
                    //Format is generally something like `cmp [r9+rax*8], r10`, where r9 is the interface offset array we have here, rax is the current loop index, and r10 is the target interface
                    //So now check that the target interface is present.
                    if (instruction.MemoryIndexScale == 0x8 && op1 is ConstantDefinition {Value: TypeDefinition _})
                    {
                        Analysis.Actions.Add(new LocateSpecificInterfaceOffsetAction(Analysis, instruction));
                    }

                    break;
                case Mnemonic.Test:
                case Mnemonic.Cmp:
                    //Condition
                    Analysis.Actions.Add(new ComparisonAction(Analysis, instruction));
                    break;
                case Mnemonic.Sar when op0 is ConstantDefinition {Value: IntegerDivisionInProgress<Instruction> _}:
                    //SAR reg, i
                    //But value in reg is the first step of an integer division
                    //So this is step 2
                    Analysis.Actions.Add(new IntegerDivisionShiftStepAction(Analysis, instruction));
                    break;
            }
        }

        private void CheckForThreeOpInstruction(Instruction instruction)
        {
            var r0 = Utils.GetRegisterNameNew(instruction.Op0Register);
            var r1 = Utils.GetRegisterNameNew(instruction.Op1Register);
            var r2 = Utils.GetRegisterNameNew(instruction.Op2Register);
            var memR = Utils.GetRegisterNameNew(instruction.MemoryBase);

            var op0 = Analysis.GetOperandInRegister(r0);
            var op1 = Analysis.GetOperandInRegister(r1);
            var op2 = Analysis.GetOperandInRegister(r2);
            var memOp = Analysis.GetOperandInRegister(memR);
            var memIdxOp = instruction.MemoryIndex == Register.None ? null : Analysis.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.MemoryIndex));

            var offset0 = instruction.MemoryDisplacement32;
            var offset1 = offset0;
            var offset2 = offset1;

            var type0 = instruction.Op0Kind;
            var type1 = instruction.Op1Kind;
            var type2 = instruction.Op2Kind;

            switch (instruction.Mnemonic)
            {
                case Mnemonic.Imul:
                    Analysis.Actions.Add(new ThreeOperandImulAction(Analysis, instruction));
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
                var loopWhichJustEnded = Analysis.GetLoopWhichJustEnded(instruction.IP);

                Analysis.IndentLevel -= 1;
                Analysis.Actions.Add(new EndWhileMarkerAction(Analysis, instruction));

                var ipOfCompare = loopWhichJustEnded!.AssociatedInstruction.IP;

                if (Analysis.Actions.FirstOrDefault(a => a.AssociatedInstruction.IP > ipOfCompare && a is BaseX86ConditionalJumpAction) is BaseX86ConditionalJumpAction jump && jump.JumpTarget != instruction.IP)
                {
                    //Once the loop is complete we have to jump to somewhere which is not here.
                    Analysis.Actions.Add(new GoToMarkerAction(Analysis, instruction, jump.JumpTarget));
                }
            }
            else if (associatedIf != 0)
            {
                Analysis.PopStashedIfDataFrom(associatedIf);

                Analysis.IndentLevel -= 1;
                Analysis.Actions.Add(new EndIfMarkerAction(Analysis, instruction, false));
            }
            else if (Analysis.GotoDestinationsToStartDict.ContainsKey(instruction.IP))
            {
                Analysis.Actions.Add(new GoToDestinationMarker(Analysis, instruction));
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
                case 3:
                    CheckForThreeOpInstruction(instruction);
                    return;
            }
        }
    }
}