using System;
using System.IO;
using System.Text;

namespace LibCpp2IL
{
    public class EndianAwareBinaryReader : BinaryReader
    {
        protected bool shouldReverseArrays = !BitConverter.IsLittleEndian; //Default to LE mode, so on LE systems, don't invert.

        public EndianAwareBinaryReader(Stream input) : base(input)
        {
        }

        public EndianAwareBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
        {
        }

        public EndianAwareBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
        {
        }

        protected void SetBigEndian()
        {
            shouldReverseArrays = BitConverter.IsLittleEndian; //Set to BE mode, so on LE systems we invert.
        }

        public override short ReadInt16()
        {
            if (!shouldReverseArrays)
                return base.ReadInt16();

            return this.ReadInt16WithReversedBits();
        }

        public override int ReadInt32()
        {
            if (!shouldReverseArrays)
                return base.ReadInt32();

            return this.ReadInt32WithReversedBits();
        }

        public override long ReadInt64()
        {
            if (!shouldReverseArrays)
                return base.ReadInt64();

            return this.ReadInt64WithReversedBits();
        }

        public override ushort ReadUInt16()
        {
            if (!shouldReverseArrays)
                return base.ReadUInt16();

            return this.ReadUInt16WithReversedBits();
        }

        public override uint ReadUInt32()
        {
            if (!shouldReverseArrays)
                return base.ReadUInt32();

            return this.ReadUInt32WithReversedBits();
        }

        public override ulong ReadUInt64()
        {
            if (!shouldReverseArrays)
                return base.ReadUInt64();

            return this.ReadUInt64WithReversedBits();
        }

        public override float ReadSingle()
        {
            if (!shouldReverseArrays)
                return base.ReadSingle();

            return this.ReadSingleWithReversedBits();
        }

        public override double ReadDouble()
        {
            if (!shouldReverseArrays)
                return base.ReadDouble();

            return this.ReadDoubleWithReversedBits();
        }
    }
}