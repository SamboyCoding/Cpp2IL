using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Core.Analysis
{
    public static class StackPointerUtils
    {
        public static LocalDefinition? GetLocalReferencedByEBPRead(MethodAnalysis context, Instruction instruction)
        {
            var offset = (int) instruction.MemoryDisplacement;
            //in 32-bit mode:
            //4 => return address
            //8 => first param
            //12 => second param, etc
            //-4 => first local
            //-8 => second local, etc
            var firstParamOffset = LibCpp2IlMain.Binary!.is32Bit ? 8 : 16;
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
                var firstLocalOffset = LibCpp2IlMain.Binary!.is32Bit ? -4 : -8;
                if (offset <= firstLocalOffset)
                {
                    //We have a local
                    offset -= firstLocalOffset;

                    var localNum = offset / firstLocalOffset;
                    if(context.StackStoredLocals.ContainsKey(localNum))
                        return context.StackStoredLocals[localNum];
                }
            }

            return null;
        }

        public static int SaveLocalToStack(MethodAnalysis context, Instruction savingInstruction, LocalDefinition theLocal)
        {
            var offset = (int) savingInstruction.MemoryDisplacement;
            //in 32-bit mode:
            //4 => return address
            //8 => first param
            //12 => second param, etc
            //-4 => first local
            //-8 => second local, etc
            var firstParamOffset = LibCpp2IlMain.Binary!.is32Bit ? 8 : 16;
            if (offset < firstParamOffset)
            {
                var firstLocalOffset = LibCpp2IlMain.Binary!.is32Bit ? -4 : -8;
                if (offset <= firstLocalOffset)
                {
                    //We have a local
                    offset -= firstLocalOffset;

                    var localNum = offset / firstLocalOffset;
                    context.StackStoredLocals[localNum] = theLocal;
                    return -(localNum + 1); //ensure we're non-zero, so we can check sign
                }
            }
            else
            {
                //turns out, space allocated for function params can be overwritten just fine by the compiler. 
                //because why wouldn't it be.
                offset -= firstParamOffset; //Subtract the base offsets

                var paramNum = offset / Utils.GetPointerSizeBytes();
                if (context.FunctionArgumentLocals.Count > paramNum)
                {
                    context.FunctionArgumentLocals[paramNum] = theLocal;
                    return (paramNum + 1);
                }
            }

            return 0;
        }
    }
}