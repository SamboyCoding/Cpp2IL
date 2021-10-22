﻿using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Code = Mono.Cecil.Cil.Code;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public class RestoreConstReferences<T> : ILPostProcessor<T> {
        public override void PostProcess(MethodAnalysis<T> analysis, MethodBody body)
        {
            var instructions = body.Instructions;

            if (body.Method.Name == "GetLoudnessByLevelId")
            {
                for (int i = 0; i < instructions.Count; i++)
                {
                    Instruction instruction = instructions[i];
                    Logger.InfoNewline(instruction.OpCode.Code.ToString());
                    Logger.InfoNewline(instruction.Operand.ToString());
                }
            }
            for (int i = 0; i < instructions.Count-1; i++)
            {
                Instruction instruction = instructions[i];
                Instruction nextInstruction = instructions[i+1];
                if (instruction.OpCode.Code == Code.Ldc_I4 && nextInstruction.OpCode.Code == Code.Ret)
                {
                    Logger.InfoNewline(analysis.DeclaringType.FullName);
                    Logger.InfoNewline(instruction.Operand.GetType().FullName);
                }
            }
        }
    }
}