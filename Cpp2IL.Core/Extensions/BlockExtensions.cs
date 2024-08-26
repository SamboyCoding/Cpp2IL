using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using System.Linq;

namespace Cpp2IL.Core.Extensions;

internal static class BlockExtensions
{
    public static void CaculateBlockType(this Block<InstructionSetIndependentInstruction> block)
    {
        // This enum is kind of redundant, can be possibly swapped for IsilFlowControl and no need for BlockType?
        if (block.Instructions.Count > 0)
        {
            var instruction = block.Instructions.Last();
            switch (instruction.FlowControl)
            {
                case IsilFlowControl.UnconditionalJump:
                    block.BlockType = BlockType.OneWay;
                    break;
                case IsilFlowControl.ConditionalJump:
                    block.BlockType = BlockType.TwoWay;
                    break;
                case IsilFlowControl.IndexedJump:
                    block.BlockType = BlockType.NWay;
                    break;
                case IsilFlowControl.MethodCall:
                    block.BlockType = BlockType.Call;
                    break;
                case IsilFlowControl.MethodReturn:
                    block.BlockType = BlockType.Return;
                    break;
                case IsilFlowControl.Interrupt:
                    block.BlockType = BlockType.Interrupt;
                    break;
                case IsilFlowControl.Continue:
                    block.BlockType = BlockType.Fall;
                    break;
                default:
                    block.BlockType = BlockType.Unknown;
                    break;
            }
        }
    }
}
