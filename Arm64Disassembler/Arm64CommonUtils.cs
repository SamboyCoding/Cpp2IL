using System.Collections;

namespace Arm64Disassembler;

/// <summary>
/// Helper functions common to various arm64 instructions.
/// The BitArray stuff in this class is Big-Endian - bit 0 is the most significant (leftmost) bit.
/// </summary>
public static class Arm64CommonUtils
{
    /// <summary>
    /// Extends the given bit array to the given length by continuously adding the leftmost bit to the left until the length is reached. 
    /// </summary>
    private static BitArray SignExtend(BitArray value, int size)
    {
        var result = new BitArray(size);
        
        //Get top bit of value
        var topBit = value[0];
        
        var startOffset = size - value.Length;
        //Copy bottom n bits of value to result
        for (var i = startOffset; i < size - 1; i++)
        {
            result[i] = value[i - startOffset];
        }

        //Populate remaining bits with top bit
        for(var i = 0; i < startOffset; i++)
        {
            result[i] = topBit;
        }

        return result;
    }

    private static long BitsToLong(BitArray bits)
    {
        var result = 0L;
        var mask = 1L << (bits.Count - 1);
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                result |= mask;
            }

            mask >>= 1;
        }

        return result;
    }

    private static BitArray LongToBits(long value, int numBits)
    {
        var bits = new BitArray(numBits);
        var mask = 1L << (numBits - 1);
        for (var i = 0; i < numBits; i++)
        {
            var isBitSet = (value & mask) != 0;
            mask >>= 1;
            bits[i] = isBitSet;
        }

        return bits;
    }

    public static long SignExtend(long original, int originalSizeBits, int newSizeBits)
    {
        var originalBits = LongToBits(original, originalSizeBits);
        var extendedBits = SignExtend(originalBits, newSizeBits);

        return BitsToLong(extendedBits);
    }
}