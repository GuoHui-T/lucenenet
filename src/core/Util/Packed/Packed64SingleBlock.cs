﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lucene.Net.Store;
using Lucene.Net.Support;


namespace Lucene.Net.Util.Packed
{
    public abstract class Packed64SingleBlock : PackedInts.Mutable
    {
        public const int MAX_SUPPORTED_BITS_PER_VALUE = 32;

        private static readonly int[] SUPPORTED_BITS_PER_VALUE = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 16, 21, 32 };

        public static bool IsSupported(int bitsPerValue)
        {
            return Array.BinarySearch(SUPPORTED_BITS_PER_VALUE, bitsPerValue) >= 0;
        }

        private static int RequiredCapacity(int valueCount, int valuesPerBlock)
        {
            return valueCount / valuesPerBlock
                   + (valueCount % valuesPerBlock == 0 ? 0 : 1);
        }

        internal readonly long[] blocks;

        private Packed64SingleBlock(int valueCount, int bitsPerValue)
            : base(valueCount, bitsPerValue)
        {
            int valuesPerBlock = 64 / bitsPerValue;
            blocks = new long[RequiredCapacity(valueCount, valuesPerBlock)];
        }

        public override void Clear()
        {
            Arrays.Fill(blocks, 0L);
        }
        
        public override long RamBytesUsed()
        {
            return RamUsageEstimator.AlignObjectSize(
                RamUsageEstimator.NUM_BYTES_OBJECT_HEADER
                + 2 * RamUsageEstimator.NUM_BYTES_INT // valueCount,bitsPerValue
                + RamUsageEstimator.NUM_BYTES_OBJECT_REF) // blocks ref
                   + RamUsageEstimator.SizeOf(blocks);
        }
        
        public override int Get(int index, long[] arr, int off, int len)
        {
            len = Math.Min(len, valueCount - index);

            int originalIndex = index;

            // go to the next block boundary
            int valuesPerBlock = 64 / bitsPerValue;
            int offsetInBlock = index % valuesPerBlock;
            if (offsetInBlock != 0)
            {
                for (int i = offsetInBlock; i < valuesPerBlock && len > 0; ++i)
                {
                    arr[off++] = Get(index++);
                    --len;
                }
                if (len == 0)
                {
                    return index - originalIndex;
                }
            }

            // bulk get

            PackedInts.IDecoder decoder = BulkOperation.Of(PackedInts.Format.PACKED_SINGLE_BLOCK, bitsPerValue);

            int blockIndex = index / valuesPerBlock;
            int nblocks = (index + len) / valuesPerBlock - blockIndex;
            decoder.Decode(blocks, blockIndex, arr, off, nblocks);
            int diff = nblocks * valuesPerBlock;
            index += diff;
            len -= diff;

            if (index > originalIndex)
            {
                // stay at the block boundary
                return index - originalIndex;
            }
            else
            {
                // no progress so far => already at a block boundary but no full block to
                // get

                return base.Get(index, arr, off, len);
            }
        }


        public override int Set(int index, long[] arr, int off, int len)
        {
            len = Math.Min(len, valueCount - index);

            int originalIndex = index;

            // go to the next block boundary
            int valuesPerBlock = 64 / bitsPerValue;
            int offsetInBlock = index % valuesPerBlock;
            if (offsetInBlock != 0)
            {
                for (int i = offsetInBlock; i < valuesPerBlock && len > 0; ++i)
                {
                    Set(index++, arr[off++]);
                    --len;
                }
                if (len == 0)
                {
                    return index - originalIndex;
                }
            }

            // bulk set

            BulkOperation op = BulkOperation.Of(PackedInts.Format.PACKED_SINGLE_BLOCK, bitsPerValue);

            int blockIndex = index / valuesPerBlock;
            int nblocks = (index + len) / valuesPerBlock - blockIndex;
            op.Encode(arr, off, blocks, blockIndex, nblocks);
            int diff = nblocks * valuesPerBlock;
            index += diff;
            len -= diff;

            if (index > originalIndex)
            {
                // stay at the block boundary
                return index - originalIndex;
            }
            else
            {
                // no progress so far => already at a block boundary but no full block to
                // set

                return base.Set(index, arr, off, len);
            }
        }


        public override void Fill(int fromIndex, int toIndex, long val)
        {
            int valuesPerBlock = 64 / bitsPerValue;
            if (toIndex - fromIndex <= valuesPerBlock << 1)
            {
                // there needs to be at least one full block to set for the block
                // approach to be worth trying
                base.Fill(fromIndex, toIndex, val);
                return;
            }

            // set values naively until the next block start
            int fromOffsetInBlock = fromIndex % valuesPerBlock;
            if (fromOffsetInBlock != 0)
            {
                for (int i = fromOffsetInBlock; i < valuesPerBlock; ++i)
                {
                    Set(fromIndex++, val);
                }
            }

            // bulk set of the inner blocks
            int fromBlock = fromIndex / valuesPerBlock;
            int toBlock = toIndex / valuesPerBlock;

            long blockValue = 0L;
            for (int i = 0; i < valuesPerBlock; ++i)
            {
                blockValue = blockValue | (val << (i * bitsPerValue));
            }
            Arrays.Fill(blocks, fromBlock, toBlock, blockValue);

            // fill the gap
            for (int i = valuesPerBlock * toBlock; i < toIndex; ++i)
            {
                Set(i, val);
            }
        }


        protected override PackedInts.Format Format
        {
            get
            {
                return PackedInts.Format.PACKED_SINGLE_BLOCK;
            }
        }

        public override String ToString()
        {
            return GetType().Name + "(bitsPerValue=" + bitsPerValue
                   + ", size=" + Size() + ", elements.length=" + blocks.Length + ")";
        }

        public static Packed64SingleBlock Create(DataInput input,
                                                 int valueCount, int bitsPerValue)
        {
            Packed64SingleBlock reader = Create(valueCount, bitsPerValue);
            for (int i = 0; i < reader.blocks.Length; ++i)
            {
                reader.blocks[i] = input.ReadLong();
            }
            return reader;
        }


        public static Packed64SingleBlock Create(int valueCount, int bitsPerValue)
        {
            switch (bitsPerValue)
            {
                case 1:
                    return new Packed64SingleBlock1(valueCount);
                case 2:
                    return new Packed64SingleBlock2(valueCount);
                case 3:
                    return new Packed64SingleBlock3(valueCount);
                case 4:
                    return new Packed64SingleBlock4(valueCount);
                case 5:
                    return new Packed64SingleBlock5(valueCount);
                case 6:
                    return new Packed64SingleBlock6(valueCount);
                case 7:
                    return new Packed64SingleBlock7(valueCount);
                case 8:
                    return new Packed64SingleBlock8(valueCount);
                case 9:
                    return new Packed64SingleBlock9(valueCount);
                case 10:
                    return new Packed64SingleBlock10(valueCount);
                case 12:
                    return new Packed64SingleBlock12(valueCount);
                case 16:
                    return new Packed64SingleBlock16(valueCount);
                case 21:
                    return new Packed64SingleBlock21(valueCount);
                case 32:
                    return new Packed64SingleBlock32(valueCount);
                default:
                    throw new ArgumentException("Unsupported number of bits per value: " + 32);
            }
        }


        internal class Packed64SingleBlock1 : Packed64SingleBlock
        {
            public Packed64SingleBlock1(int valueCount)
                : base(valueCount, 1)
            {
            }


            public override long Get(int index)
            {
                int o = Support.Number.URShift(index, 6);
                int b = index & 63;
                int shift = b << 0;
                return Number.URShift(blocks[o], shift) & 1L;
            }

            public override void Set(int index, long value)
            {
                int o = Support.Number.URShift(index, 6);
                int b = index & 63;
                int shift = b << 0;
                blocks[o] = (blocks[o] & ~(1L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock2 : Packed64SingleBlock
        {
            public Packed64SingleBlock2(int valueCount)
                : base(valueCount, 2)
            {
            }

            public override long Get(int index)
            {
                int o = Number.URShift(index, 5);
                int b = index & 31;
                int shift = b << 1;
                return Number.URShift(blocks[o], shift) & 3L;
            }

            public override void Set(int index, long value)
            {
                int o = Number.URShift(index, 5);
                int b = index & 31;
                int shift = b << 1;
                blocks[o] = (blocks[o] & ~(3L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock3 : Packed64SingleBlock
        {
            public Packed64SingleBlock3(int valueCount)
                : base(valueCount, 3)
            {
            }

            public override long Get(int index)
            {
                int o = index / 21;
                int b = index % 21;
                int shift = b * 3;
                return Number.URShift(blocks[o], shift) & 7L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 21;
                int b = index % 21;
                int shift = b * 3;
                blocks[o] = (blocks[o] & ~(7L << shift)) | (value << shift);
            }
        }


        internal class Packed64SingleBlock4 : Packed64SingleBlock
        {
            public Packed64SingleBlock4(int valueCount)
                : base(valueCount, 4)
            {
            }

            public override long Get(int index)
            {
                int o = Number.URShift(index, 4);
                int b = index & 15;
                int shift = b << 2;
                return Number.URShift(blocks[o], shift) & 15L;
            }

            public override void Set(int index, long value)
            {
                int o = Number.URShift(index, 4);
                int b = index & 15;
                int shift = b << 2;
                blocks[o] = (blocks[o] & ~(15L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock5 : Packed64SingleBlock
        {
            public Packed64SingleBlock5(int valueCount)
                : base(valueCount, 5)
            {
            }

            public override long Get(int index)
            {
                int o = index / 12;
                int b = index % 12;
                int shift = b * 5;
                return Number.URShift(blocks[o], shift) & 31L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 12;
                int b = index % 12;
                int shift = b * 5;
                blocks[o] = (blocks[o] & ~(31L << shift)) | (value << shift);
            }
        }


        internal class Packed64SingleBlock6 : Packed64SingleBlock
        {
            public Packed64SingleBlock6(int valueCount)
                : base(valueCount, 6)
            {
            }

            public override long Get(int index)
            {
                int o = index / 10;
                int b = index % 10;
                int shift = b * 6;
                return Number.URShift(blocks[o], shift) & 63L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 10;
                int b = index % 10;
                int shift = b * 6;
                blocks[o] = (blocks[o] & ~(63L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock7 : Packed64SingleBlock
        {
            public Packed64SingleBlock7(int valueCount)
                : base(valueCount, 7)
            {
            }

            public override long Get(int index)
            {
                int o = index / 9;
                int b = index % 9;
                int shift = b * 7;
                return Number.URShift(blocks[o], shift) & 127L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 9;
                int b = index % 9;
                int shift = b * 7;
                blocks[o] = (blocks[o] & ~(127L << shift)) | (value << shift);
            }
        }


        internal class Packed64SingleBlock8 : Packed64SingleBlock
        {
            public Packed64SingleBlock8(int valueCount)
                : base(valueCount, 8)
            {
            }

            public override long Get(int index)
            {
                int o = Number.URShift(index, 3);
                int b = index & 7;
                int shift = b << 3;
                return Number.URShift(blocks[o], shift) & 255L;
            }

            public override void Set(int index, long value)
            {
                int o = Number.URShift(index, 3);
                int b = index & 7;
                int shift = b << 3;
                blocks[o] = (blocks[o] & ~(255L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock9 : Packed64SingleBlock
        {
            public Packed64SingleBlock9(int valueCount)
                : base(valueCount, 9)
            {
            }

            public override long Get(int index)
            {
                int o = index / 7;
                int b = index % 7;
                int shift = b * 9;
                return Number.URShift(blocks[o], shift) & 511L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 7;
                int b = index % 7;
                int shift = b * 9;
                blocks[o] = (blocks[o] & ~(511L << shift)) | (value << shift);
            }
        }


        internal class Packed64SingleBlock10 : Packed64SingleBlock
        {
            public Packed64SingleBlock10(int valueCount)
                : base(valueCount, 10)
            {
            }

            public override long Get(int index)
            {
                int o = index / 6;
                int b = index % 6;
                int shift = b * 10;
                return Number.URShift(blocks[o], shift) & 1023L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 6;
                int b = index % 6;
                int shift = b * 10;
                blocks[o] = (blocks[o] & ~(1023L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock12 : Packed64SingleBlock
        {
            public Packed64SingleBlock12(int valueCount)
                : base(valueCount, 12)
            {
            }

            public override long Get(int index)
            {
                int o = index / 5;
                int b = index % 5;
                int shift = b * 12;
                return Number.URShift(blocks[o], shift) & 4095L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 5;
                int b = index % 5;
                int shift = b * 12;
                blocks[o] = (blocks[o] & ~(4095L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock16 : Packed64SingleBlock
        {
            public Packed64SingleBlock16(int valueCount)
                : base(valueCount, 16)
            {
            }

            public override long Get(int index)
            {
                int o = Number.URShift(index, 2);
                int b = index & 3;
                int shift = b << 4;
                return Number.URShift(blocks[o], shift) & 65535L;
            }

            public override void Set(int index, long value)
            {
                int o = Number.URShift(index, 2);
                int b = index & 3;
                int shift = b << 4;
                blocks[o] = (blocks[o] & ~(65535L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock21 : Packed64SingleBlock
        {
            public Packed64SingleBlock21(int valueCount)
                : base(valueCount, 21)
            {
            }

            public override long Get(int index)
            {
                int o = index / 3;
                int b = index % 3;
                int shift = b * 21;
                return Number.URShift(blocks[o], shift) & 2097151L;
            }

            public override void Set(int index, long value)
            {
                int o = index / 3;
                int b = index % 3;
                int shift = b * 21;
                blocks[o] = (blocks[o] & ~(2097151L << shift)) | (value << shift);
            }
        }

        internal class Packed64SingleBlock32 : Packed64SingleBlock
        {
            public Packed64SingleBlock32(int valueCount)
                : base(valueCount, 32)
            {
            }

            public override long Get(int index)
            {
                int o = Number.URShift(index, 1);
                int b = index & 1;
                int shift = b << 5;
                return Number.URShift(blocks[o], shift) & 4294967295L;
            }

            public override void Set(int index, long value)
            {
                int o = Number.URShift(index, 1);
                int b = index & 1;
                int shift = b << 5;
                blocks[o] = (blocks[o] & ~(4294967295L << shift)) | (value << shift);
            }
        }

        public abstract override void Set(int index, long value);

        public abstract override long Get(int index);
    }
}