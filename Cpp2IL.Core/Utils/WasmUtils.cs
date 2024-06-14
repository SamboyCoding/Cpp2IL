using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
using LibCpp2IL.Wasm;
using WasmDisassembler;

namespace Cpp2IL.Core.Utils
{
    public static class WasmUtils
    {
        internal static readonly Dictionary<int, List<Il2CppMethodDefinition>> MethodDefinitionIndices = new();
        private static Regex DynCallRemappingRegex = new(@"Module\[\s*[""'](dynCall_[^""']+)[""']\s*\]\s*=\s*Module\[\s*[""']asm[""']\s*\]\[\s*[""']([^""']+)[""']\s*\]\s*\)\.apply", RegexOptions.Compiled);

        public static string BuildSignature(Il2CppMethodDefinition definition)
        {
            var instanceParam = definition.IsStatic ? "" : "i";
            
            //Something still off about p/invoke functions. They do have methodinfo args, but something is wrong somewhere.
            
            return $"{GetSignatureLetter(definition.ReturnType!)}{instanceParam}{string.Join("", definition.Parameters!.Select(p => GetSignatureLetter(p.Type, p.IsRefOrOut)))}i"; //Add an extra i on the end for the method info param
        }

        private static char GetSignatureLetter(Il2CppTypeReflectionData type, bool isRefOrOut = false)
        {
            if(isRefOrOut)
                //ref/out params are passed as pointers 
                return 'i';

            if (type.isPointer)
                //Pointers are ints
                return 'i';

            var typeDefinition = type.baseType ?? LibCpp2IlReflection.GetType("Int32", "System")!;

            return typeDefinition.Name switch
            {
                "Void" => 'v',
                "Int64" => 'j',
                "Single" => 'f',
                "Double" => 'd',
                _ => 'i' //Including Int32
            };
        }

        public static string GetGhidraFunctionName(WasmFunctionDefinition functionDefinition)
        {
            var index = functionDefinition.IsImport 
                ? ((WasmFile) LibCpp2IlMain.Binary!).FunctionTable.IndexOf(functionDefinition) 
                : functionDefinition.FunctionTableIndex;
            
            return $"unnamed_function_{index}";
        }

        public static WasmFunctionDefinition? TryGetWasmDefinition(Il2CppMethodDefinition definition)
        {
            try
            {
                return GetWasmDefinition(definition);
            }
            catch
            {
                return null;
            }
        }

        public static WasmFunctionDefinition GetWasmDefinition(Il2CppMethodDefinition definition)
        {
            //First, we have to calculate the signature
            var signature = BuildSignature(definition);
            try
            {
                return ((WasmFile) LibCpp2IlMain.Binary!).GetFunctionFromIndexAndSignature(definition.MethodPointer, signature);
            }
            catch (Exception e)
            {
                throw new($"Failed to find wasm definition for {definition}\nwhich has params {definition.Parameters?.ToStringEnumerable()}", e);
            }
        }

        private static void CalculateAllMethodDefinitionIndices()
        {
            foreach (var il2CppMethodDefinition in LibCpp2IlMain.TheMetadata!.methodDefs)
            {
                var methodDefinition = il2CppMethodDefinition;

                try
                {
                    var wasmDef = GetWasmDefinition(methodDefinition);
                    var index = ((WasmFile) LibCpp2IlMain.Binary!).FunctionTable.IndexOf(wasmDef);

                    if (!MethodDefinitionIndices.TryGetValue(index, out var mDefs))
                        MethodDefinitionIndices[index] = mDefs = new();

                    mDefs.Add(methodDefinition);
                }
                catch (Exception)
                {
                    //Ignore
                }
            }
        }

        public static List<Il2CppMethodDefinition>? GetMethodDefinitionsAtIndex(int index)
        {
            if(MethodDefinitionIndices.Count == 0)
                CalculateAllMethodDefinitionIndices();

            if (MethodDefinitionIndices.TryGetValue(index, out var methodDefinitions))
                return methodDefinitions;

            return null;
        }

        public static Dictionary<string, string> ExtractAndParseDynCallRemaps(string frameworkJsFile)
        {
            //At least one WASM binary found in the wild had the exported function names obfuscated.
            //However, the framework.js file has mappings to the correct names.
            /*e.g.
             var dynCall_viffiiii = Module["dynCall_viffiiii"] = function() {
                return (dynCall_viffiiii = Module["dynCall_viffiiii"] = Module["asm"]["Wo"]).apply(null, arguments)
             }
            */
            
            var ret = new Dictionary<string, string>();
            var matches = DynCallRemappingRegex.Matches(frameworkJsFile);
            foreach (Match match in matches)
            {
                //Group 1 is the original method name, e.g. dynCall_viffiiii
                //Group 2 is the remapped name, e.g Wo
                var origName = match.Groups[1];
                var remappedName = match.Groups[2];
                
                ret[remappedName.Value] = origName.Value;
            }

            return ret;
        }
        
        public static void GenerateBodyForMethod(MethodAnalysisContext analysisContext, MethodDefinition method)
        {
            method.CilMethodBody = new CilMethodBody(method);
            var managedinstrs = method.CilMethodBody.Instructions;
        
            var wasmdef = TryGetWasmDefinition(analysisContext.Definition);
            if (wasmdef is null) return;
            var wasminstrs = Disassembler.Disassemble(wasmdef.AssociatedFunctionBody?.Instructions, (uint)analysisContext.UnderlyingPointer);
        
            foreach (var instr in wasminstrs)
            {
                switch (instr.Mnemonic)
                {
                    case WasmMnemonic.Unreachable:
                        break;
                    case WasmMnemonic.Nop:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Nop));
                        break;
                    case WasmMnemonic.Block:
                        break;
                    case WasmMnemonic.Loop:
                        break;
                    case WasmMnemonic.If:
                        break;
                    case WasmMnemonic.Else:
                        break;
                    case WasmMnemonic.Proposed_Try:
                        break;
                    case WasmMnemonic.Proposed_Catch:
                        break;
                    case WasmMnemonic.Proposed_Throw:
                        break;
                    case WasmMnemonic.Proposed_Rethrow:
                        break;
                    case WasmMnemonic.Proposed_BrOnExn:
                        break;
                    case WasmMnemonic.End:
                        break;
                    case WasmMnemonic.Br:
                        break;
                    case WasmMnemonic.BrIf:
                        break;
                    case WasmMnemonic.BrTable:
                        break;
                    case WasmMnemonic.Return:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ret));
                        break;
                    case WasmMnemonic.Call:
                        break;
                    case WasmMnemonic.CallIndirect:
                        break;
                    case WasmMnemonic.Proposed_ReturnCall:
                        break;
                    case WasmMnemonic.Proposed_ReturnCallIndirect:
                        break;
                    case WasmMnemonic.Reserved_14:
                        break;
                    case WasmMnemonic.Reserved_15:
                        break;
                    case WasmMnemonic.Reserved_16:
                        break;
                    case WasmMnemonic.Reserved_17:
                        break;
                    case WasmMnemonic.Reserved_18:
                        break;
                    case WasmMnemonic.Reserved_19:
                        break;
                    case WasmMnemonic.Drop:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Pop));
                        break;
                    case WasmMnemonic.Select:
                        break;    
                    case WasmMnemonic.Proposed_SelectT:
                        break;
                    case WasmMnemonic.Reserved_1D:
                        break; 
                    case WasmMnemonic.Reserved_1E:
                        break;
                    case WasmMnemonic.Reserved_1F:
                        break;
                    case WasmMnemonic.LocalGet:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldarg, instr.Operands[0])); // todo: optimized form _0, _1
                        break;
                    case WasmMnemonic.LocalSet:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Starg, instr.Operands[0]));
                        break;
                    case WasmMnemonic.LocalTee:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I4, instr.Operands[0])); // todo: operand is int16, il doesn't care? chk
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Starg, instr.Operands[0]));
                        break;
                    case WasmMnemonic.GlobalGet:
                        break;
                    case WasmMnemonic.GlobalSet:
                        break;
                    case WasmMnemonic.Proposed_TableGet:
                        break;
                    case WasmMnemonic.Proposed_TableSet:
                        break;
                    case WasmMnemonic.Reserved_27:
                        break;
                    case WasmMnemonic.I32Load8_S:
                        break;
                    case WasmMnemonic.I32Load8_U:
                        break;
                    case WasmMnemonic.I32Load16_S:
                        break;
                    case WasmMnemonic.I32Load16_U:
                        break;
                    case WasmMnemonic.I64Load8_S:
                        break;
                    case WasmMnemonic.I64Load8_U:
                        break;
                    case WasmMnemonic.I64Load16_S:
                        break;
                    case WasmMnemonic.I64Load16_U:
                        break;
                    case WasmMnemonic.I64Load32_S:
                        break;
                    case WasmMnemonic.I64Load32_U:
                        break;
                    case WasmMnemonic.I32Store:
                        break;
                    case WasmMnemonic.I64Store:
                        break;
                    case WasmMnemonic.F32Store:
                        break;
                    case WasmMnemonic.F64Store:
                        break;
                    case WasmMnemonic.I32Store8:
                        break;
                    case WasmMnemonic.I32Store16:
                        break;
                    case WasmMnemonic.I64Store8:
                        break;
                    case WasmMnemonic.I64Store16:
                        break;
                    case WasmMnemonic.I64Store32:
                        break;
                    case WasmMnemonic.MemorySize:
                        break;
                    case WasmMnemonic.MemoryGrow:
                        break;
                    case WasmMnemonic.I32Const:
                        managedinstrs.Add(CilInstruction.CreateLdcI4((int)instr.Operands[0]));
                        break;
                    case WasmMnemonic.I64Const:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I8, instr.Operands[0]));
                        break;
                    case WasmMnemonic.F32Const:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_R4, instr.Operands[0]));
                        break;
                    case WasmMnemonic.F64Const:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_R8, instr.Operands[0]));
                        break;
                    // numerics
                    case WasmMnemonic.I32Load: // TODO
                        break;
                    case WasmMnemonic.I64Load:
                        break;
                    case WasmMnemonic.F32Load:
                        break;
                    case WasmMnemonic.F64Load:
                        break;
                    case WasmMnemonic.I32Eqz:
                    case WasmMnemonic.I64Eqz: // todo: make sure not conving is valid
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I4_0));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        break;
                    case WasmMnemonic.I32Eq:
                    case WasmMnemonic.I64Eq:
                    case WasmMnemonic.F32Eq:
                    case WasmMnemonic.F64Eq:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        break;
                    case WasmMnemonic.I32Ne:
                    case WasmMnemonic.I64Ne:
                    case WasmMnemonic.F32Ne:
                    case WasmMnemonic.F64Ne:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I4_0));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        break;
                    case WasmMnemonic.I32Lt_S:
                    case WasmMnemonic.I64Lt_S:
                    case WasmMnemonic.F32Lt:
                    case WasmMnemonic.F64Lt:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Clt));
                        break;
                    case WasmMnemonic.I32Lt_U:
                    case WasmMnemonic.I64Lt_U:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Clt_Un));
                        break;
                    case WasmMnemonic.I32Gt_S:
                    case WasmMnemonic.I64Gt_S:
                    case WasmMnemonic.F32Gt:
                    case WasmMnemonic.F64Gt:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Cgt));
                        break;
                    case WasmMnemonic.I32Gt_U:
                    case WasmMnemonic.I64Gt_U:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Cgt_Un));
                        break;
                    case WasmMnemonic.I32Le_S:
                    case WasmMnemonic.I64Le_S:
                    case WasmMnemonic.F32Le: // TODO: figure out why compiler prefers clt.un even though floats are signed
                    case WasmMnemonic.F64Le:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Cgt));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I4_0));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        break;
                    case WasmMnemonic.I32Le_U:
                    case WasmMnemonic.I64Le_U:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Cgt_Un));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I4_0));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        break;
                    case WasmMnemonic.I32Ge_S:
                    case WasmMnemonic.I64Ge_S:
                    case WasmMnemonic.F32Ge: // see above comment
                    case WasmMnemonic.F64Ge:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Clt));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I4_0));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        break;
                    case WasmMnemonic.I32Ge_U:
                    case WasmMnemonic.I64Ge_U:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Clt_Un));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ldc_I4_0));
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Ceq));
                        break;
                    case WasmMnemonic.I32Clz: // TODO
                        break;
                    case WasmMnemonic.I32Ctz:
                        break;
                    case WasmMnemonic.I32PopCnt:
                        break;
                    case WasmMnemonic.I32Add:
                    case WasmMnemonic.I64Add:
                    case WasmMnemonic.F32Add:
                    case WasmMnemonic.F64Add:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Add));
                        break;
                    case WasmMnemonic.I32Sub:
                    case WasmMnemonic.I64Sub:
                    case WasmMnemonic.F32Sub:
                    case WasmMnemonic.F64Sub:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Sub));
                        break;
                    case WasmMnemonic.I32Mul:
                    case WasmMnemonic.I64Mul:
                    case WasmMnemonic.F32Mul:
                    case WasmMnemonic.F64Mul:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Mul));
                        break;
                    case WasmMnemonic.I32Div_S:
                    case WasmMnemonic.I64Div_S:
                    case WasmMnemonic.F32Div:
                    case WasmMnemonic.F64Div:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Div));
                        break;
                    case WasmMnemonic.I32Div_U:
                    case WasmMnemonic.I64Div_U:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Div_Un));
                        break;
                    case WasmMnemonic.I32Rem_S:
                    case WasmMnemonic.I64Rem_S:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Rem));
                        break;
                    case WasmMnemonic.I32Rem_U:
                    case WasmMnemonic.I64Rem_U:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Rem_Un));
                        break;
                    case WasmMnemonic.I32And:
                    case WasmMnemonic.I64And:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.And));
                        break;
                    case WasmMnemonic.I32Or:
                    case WasmMnemonic.I64Or:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Or));
                        break;
                    case WasmMnemonic.I32Xor:
                    case WasmMnemonic.I64Xor:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Xor));
                        break;
                    case WasmMnemonic.I32Shl:
                    case WasmMnemonic.I64Shl:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Shl));
                        break;
                    case WasmMnemonic.I32Shr_S:
                    case WasmMnemonic.I64Shr_S:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Shr));
                        break;
                    case WasmMnemonic.I32Shr_U:
                    case WasmMnemonic.I64Shr_U:
                        managedinstrs.Add(new CilInstruction(CilOpCodes.Shr_Un));
                        break;
                    case WasmMnemonic.I32Rotl:
                        break;
                    case WasmMnemonic.I32Rotr:
                        break;
                    case WasmMnemonic.I64Clz:
                        break;
                    case WasmMnemonic.I64Ctz:
                        break;
                    case WasmMnemonic.I64PopCnt:
                        break;
                    case WasmMnemonic.I64Rotl:
                        break;
                    case WasmMnemonic.I64Rotr:
                        break;
                    case WasmMnemonic.F32Abs:
                        break;
                    case WasmMnemonic.F32Neg:
                        break;
                    case WasmMnemonic.F32Ceil:
                        break;
                    case WasmMnemonic.F32Floor:
                        break;
                    case WasmMnemonic.F32Trunc:
                        break;
                    case WasmMnemonic.F32Nearest:
                        break;
                    case WasmMnemonic.F32Sqrt:
                        break;
                    case WasmMnemonic.F32Min:
                        break;
                    case WasmMnemonic.F32Max:
                        break;
                    case WasmMnemonic.F32Copysign:
                        break;
                    case WasmMnemonic.F64Abs:
                        break;
                    case WasmMnemonic.F64Neg:
                        break;
                    case WasmMnemonic.F64Ceil:
                        break;
                    case WasmMnemonic.F64Floor:
                        break;
                    case WasmMnemonic.F64Trunc:
                        break;
                    case WasmMnemonic.F64Nearest:
                        break;
                    case WasmMnemonic.F64Sqrt:
                        break;
                    case WasmMnemonic.F64Min:
                        break;
                    case WasmMnemonic.F64Max:
                        break;
                    case WasmMnemonic.F64Copysign:
                        break;
                    case WasmMnemonic.I32Wrap_I64:
                        break;
                    case WasmMnemonic.I32Trunc_F32_S:
                        break;
                    case WasmMnemonic.I32Trunc_F32_U:
                        break;
                    case WasmMnemonic.I32Trunc_F64_S:
                        break;
                    case WasmMnemonic.I32Trunc_F64_U:
                        break;
                    case WasmMnemonic.I64Extend_I32_S:
                        break;
                    case WasmMnemonic.I64Extend_I32_U:
                        break;
                    case WasmMnemonic.I64Trunc_F32_S:
                        break;
                    case WasmMnemonic.I64Trunc_F32_U:
                        break;
                    case WasmMnemonic.I64Trunc_F64_S:
                        break;
                    case WasmMnemonic.I64Trunc_F64_U:
                        break;
                    case WasmMnemonic.F32Convert_I32_S:
                        break;
                    case WasmMnemonic.F32Convert_I32_U:
                        break;
                    case WasmMnemonic.F32Convert_I64_S:
                        break;
                    case WasmMnemonic.F32Convert_I64_U:
                        break;
                    case WasmMnemonic.F32Demote_F64:
                        break;
                    case WasmMnemonic.F64Convert_I32_S:
                        break;
                    case WasmMnemonic.F64Convert_I32_U:
                        break;
                    case WasmMnemonic.F64Convert_I64_S:
                        break;
                    case WasmMnemonic.F64Convert_I64_U:
                        break;
                    case WasmMnemonic.F64Promote_F32:
                        break;
                    case WasmMnemonic.I32Reinterpret_F32:
                        break;
                    case WasmMnemonic.I64Reinterpret_F64:
                        break;
                    case WasmMnemonic.F32Reinterpret_I32:
                        break;
                    case WasmMnemonic.F64Reinterpret_I64:
                        break;
                    case WasmMnemonic.Proposed_I32Extend8_S:
                        break;
                    case WasmMnemonic.Proposed_I32Extend16_S:
                        break;
                    case WasmMnemonic.Proposed_I64Extend8_S:
                        break;
                    case WasmMnemonic.Proposed_I64Extend16_S:
                        break;
                    case WasmMnemonic.Proposed_I64Extend32_S:
                        break;
                    case WasmMnemonic.Proposed_RefNull:
                        break;
                    case WasmMnemonic.Proposed_RefIsNull:
                        break;
                    case WasmMnemonic.Proposed_RefFunc:
                        break;
                    case WasmMnemonic.Proposed_FC_Extensions:
                        break;
                    case WasmMnemonic.Proposed_SIMD:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                
            }
        }
    }
}
