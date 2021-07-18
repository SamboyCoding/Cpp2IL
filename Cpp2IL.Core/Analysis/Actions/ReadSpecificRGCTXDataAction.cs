using System;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.BinaryStructures;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class ReadSpecificRGCTXDataAction : BaseAction
    {
        private Il2CppRGCTXArray? _rgctxArray;
        private uint _offset;
        private Il2CppRGCTXDefinition? _actualRgctx;
        private ConstantDefinition? _constant;
        private string? _destReg;
        private ConstantDefinition? _constantMade;
        private object? _dataValue;

        public ReadSpecificRGCTXDataAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            _constant = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            _rgctxArray = _constant?.Value as Il2CppRGCTXArray;
            
            if(_rgctxArray == null)
                return;

            var displacement = instruction.MemoryDisplacement;
            _offset = displacement / 8;

            if (_offset >= _rgctxArray.Rgctxs.Length)
                return;

            _actualRgctx = _rgctxArray.Rgctxs[_offset];

            switch (_actualRgctx.type)
            {
                case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_INVALID:
                    return;
                case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_TYPE:
                    break;
                case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CLASS:
                    _dataValue = Utils.TryResolveTypeReflectionData(_actualRgctx.Type, context.DeclaringType);
                    if (_dataValue != null)
                    {
                        _constantMade = context.MakeConstant(typeof(TypeReference), _dataValue, reg: _destReg);
                        return;
                    }
                    break;
                case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_METHOD:
                    _dataValue = _actualRgctx.MethodSpec;
                    if (_dataValue != null)
                    {
                        _constantMade = context.MakeConstant(typeof(Il2CppMethodSpec), _dataValue, reg: _destReg);
                        return;
                    }

                    break;
                case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_ARRAY:
                    break;
                default:
                    throw new Exception("Bad rgctx type");
            }
            
            _constantMade = context.MakeConstant(typeof(Il2CppRGCTXDefinition), _actualRgctx, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Reads the RGCTX data at index {_offset} in the array {_constant?.Name}, which has datapoint {_actualRgctx?._rawIndex} and is of type {_actualRgctx?.type} (mapping to actual value {_dataValue}), and stores the result in new constant {_constantMade?.Name} in register {_destReg}";
        }
    }
}