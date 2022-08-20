using System.Collections;

namespace Arm64Disassembler.InternalDisassembly;

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

    private static BitArray Replicate(BitArray original, int desiredLength)
    {
        if(desiredLength % original.Length != 0)
            throw new("Desired length is not a multiple of the original length");
        
        var result = new BitArray(desiredLength);
        
        for(var i = 0; i < desiredLength; i += original.Length)
        {
            for(var j = 0; j < original.Length; j++)
            {
                result[i + j] = original[j];
            }
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

    private static ulong RotateRight(ulong original, int numBits, int shift)
    {
        var m = shift % numBits;

        var right = original >> m;
        var left = original << (numBits - m);

        return right | left;
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

    private static int HighestSetBit(BitArray bits)
    {
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits.Get(i))
            {
                //Big endian -> little endian, then 0-indexed
                return (bits.Length - i) - 1;
            }
        }

        return 0;
    }

    public static long SignExtend(long original, int originalSizeBits, int newSizeBits)
    {
        var originalBits = LongToBits(original, originalSizeBits);
        var extendedBits = SignExtend(originalBits, newSizeBits);

        return BitsToLong(extendedBits);
    }

    public static int CorrectSignBit(uint original, int originalSizeBits)
    {
        var topBitMask = 1 << (originalSizeBits - 1);
        
        //Get top bit of value
        var topBit = (original & topBitMask) != 0;

        if (!topBit)
            return (int)original;

        //Negative - get remainder, and flip all bits, then subtract from -1
        //This means all bits set => -1 - 0 = -1
        //All bits clear (except sign bit) => -1 - ((2^originalSizeBits)-1) = -(2^originalSizeBits)
        var remainder = (int) original & (topBitMask - 1);

        return -1 - (~remainder & (topBitMask - 1));
    }

    public static ulong ApplyShift(ulong original, ShiftType type, int numBits, int amount)
    {
        return type switch
        {
            ShiftType.LSL => original << amount,
            ShiftType.LSR => original >> amount,
            ShiftType.ASR => (uint)((int)original >> amount),
            ShiftType.ROR => RotateRight(original, numBits, amount),
            _ => throw new ArgumentException("Unknown shift type")
        };
    }

    public static (long, long) DecodeBitMasks(bool nFlag, int desiredSize, byte imms, byte immr, bool immediate)
    {
        //imms and immr are actually 6 bits not 8.
        
        var combined = (short)((imms << 6) | (~immr & 0b11_1111));
        var bits = LongToBits(combined, 12);
        var len = HighestSetBit(bits);
        
        if(len < 1)
            throw new Arm64UndefinedInstructionException("DecodeBitMasks: highestBit < 1");
        
        if((1 << len) > desiredSize)
            throw new Arm64UndefinedInstructionException("DecodeBitMasks: (1 << highestBit) > desiredSize");
        
        var levels = (1 << len) - 1;
        
        if(immediate && (imms & levels) == levels)
            throw new Arm64UndefinedInstructionException("DecodeBitMasks: imms & levels == levels not allowed in immediate mode");

        var s = imms & levels;
        var r = immr & levels;
        var diff = s - r;
        var esize = 1 << len;

        var d = diff & ((1 << (len - 1)) - 1); //UInt(diff<len-1:0>)
        var wElem = (1 << (s + 1)) - 1;
        var tElem = (1 << (d + 1)) - 1;

        var wMask = Replicate(LongToBits((long)RotateRight((ulong)wElem, esize, r), esize), desiredSize);
        var tMask = Replicate(LongToBits(tElem, esize), desiredSize);

        return (BitsToLong(wMask), BitsToLong(tMask));
    }
}