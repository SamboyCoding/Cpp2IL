using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis
{
    public static class StackPointerUtils
    {
        public static LocalDefinition GetLocalReferencedByEBPRead(MethodAnalysis context, Instruction instruction)
        {
            var offset = (int) instruction.MemoryDisplacement;
            //in 32-bit mode:
            //4 => return address
            //8 => first param
            //12 => second param, etc
            //-4 => first local
            //-8 => second local, etc
            var firstParamOffset = LibCpp2IlMain.ThePe!.is32Bit ? 8 : 16;
            if (offset >= firstParamOffset)
            {
                offset -= firstParamOffset; //Subtract the base offsets
                
                var paramNum = offset / Utils.GetPointerSizeBytes();
                if (context.FunctionArgumentLocals.Count > paramNum)
                {
                    return context.FunctionArgumentLocals[paramNum];
                }
            }
            else
            {
                //todo locals
            }

            return null;
        }
    }
}