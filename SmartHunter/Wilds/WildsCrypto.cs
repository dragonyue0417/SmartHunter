using System;
using System.Runtime.InteropServices;

namespace SmartHunter.Wilds
{
    // AES inverse final round, derived from FIPS 197 sections 4 and 5.3.
    // Compute the substitution permutation from finite-field arithmetic.
    public sealed class WildsCrypto
    {
        static readonly byte[] inverseSubstitution = CreateInverseSubstitution();
        readonly byte[] whitening, finalRound;
        readonly byte[] block = new byte[16];

        [StructLayout(LayoutKind.Explicit)]
        struct FloatBits
        {
            [FieldOffset(0)] public uint Bits;
            [FieldOffset(0)] public float Value;
        }

        public WildsCrypto(byte[] key, byte[] round)
        {
            if (key?.Length != 16 || round?.Length != 16)
                throw new ArgumentException("The game requires two 128-bit health vectors.");
            whitening = (byte[])key.Clone();
            finalRound = (byte[])round.Clone();
        }

        public float Decode(byte[] encrypted)
        {
            if (encrypted?.Length != 16)
                return float.NaN;
            uint bits = 0;
            // A single occupies column zero. Inverse row rotation determines
            // the source byte for each row; no game-specific lookup is needed.
            for (int row = 0; row != 4; ++row)
            {
                int sourceColumn = (4 - row) & 3;
                int sourceByte = 4 * sourceColumn + row;
                uint decoded = (uint)(inverseSubstitution[encrypted[sourceByte] ^ whitening[sourceByte]] ^ finalRound[row]);
                bits |= decoded << (8 * row);
            }
            return new FloatBits { Bits = bits }.Value;
        }

        public float Read(WildsMemoryView memory, ulong address)
        {
            return address != 0 && memory.ReadInto(checked(address + 16), block, block.Length)
                ? Decode(block) : float.NaN;
        }

        static byte Product(byte left, byte right)
        {
            int a = left, b = right, product = 0;
            while (b != 0)
            {
                if ((b & 1) != 0)
                    product ^= a;
                a <<= 1;
                if ((a & 256) != 0)
                    a ^= 0x11B;
                b >>= 1;
            }
            return (byte)product;
        }

        static byte Reciprocal(byte element)
        {
            if (element == 0)
                return 0;
            byte result = 1, power = element;
            for (int exponent = 254; exponent != 0; exponent >>= 1)
            {
                if ((exponent & 1) != 0)
                    result = Product(result, power);
                power = Product(power, power);
            }
            return result;
        }

        static byte[] CreateInverseSubstitution()
        {
            var permutation = new byte[256];
            for (int element = 0; element < permutation.Length; ++element)
            {
                byte reciprocal = Reciprocal((byte)element);
                int transformed = reciprocal ^ 0x63;
                for (int rotation = 1; rotation <= 4; ++rotation)
                    transformed ^= ((reciprocal << rotation) | (reciprocal >> (8 - rotation))) & 255;
                permutation[transformed] = (byte)element;
            }
            return permutation;
        }
    }
}
