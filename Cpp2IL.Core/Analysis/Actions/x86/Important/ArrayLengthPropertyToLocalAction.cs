﻿using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ArrayLengthPropertyToLocalAction : BaseAction<Instruction>
    {
        private static readonly MethodDefinition GetLengthDef = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Array")!.Methods.Single(m => m.Name == "get_Length");
        public LocalDefinition? LocalMade;
        public LocalDefinition? TheArray;
        private string? _destReg;

        public ArrayLengthPropertyToLocalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var memReg = X86Utils.GetRegisterNameNew(instruction.MemoryBase);
            TheArray = context.GetLocalInReg(memReg);

            if (TheArray?.Type?.IsArray != true)
                return;

            _destReg = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            LocalMade = context.MakeLocal(MiscUtils.Int32Reference, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (LocalMade == null || TheArray == null)
                throw new TaintedInstructionException("Array or destination is null");

            if (LocalMade.Variable == null)
                //Stripped out - couldn't find a usage for this local.
                return Array.Empty<Mono.Cecil.Cil.Instruction>();

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            //Load array
            ret.AddRange(TheArray.GetILToLoad(context, processor));

            //Call get_Length
            ret.Add(processor.Create(OpCodes.Call, processor.ImportReference(GetLengthDef)));
            
            //Store length in local
            ret.Add(processor.Create(OpCodes.Stloc, LocalMade.Variable));

            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"System.Int32 {LocalMade?.GetPseudocodeRepresentation()} = {TheArray?.GetPseudocodeRepresentation()}.Length";
        }

        public override string ToTextSummary()
        {
            return $"Reads the length of the array \"{TheArray}\" and stores it in new local {LocalMade} in register {_destReg}";
        }

        public override bool IsImportant() => true;
    }
}