using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public struct SHA1Hash
    {
        public readonly byte[] Hash;

        public SHA1Hash(byte[] hash)
        {
            if (hash == null) throw new ArgumentNullException("hash");
            if (hash.Length != 20) throw new ArgumentOutOfRangeException("hash", "SHA1Hash must be 20 bytes in length");
            Hash = hash;
        }

        public override string ToString()
        {
            return ToHexString();
        }

        const string hexChars = "0123456789abcdef";

        public string ToHexString(int length = -1)
        {
            if (length == 0) return String.Empty;
            if (length < 0) length = Hash.Length * 2;

            bool isEven = (length & 1) == 0;
            int arrLength = length / 2;

            var sb = new StringBuilder(length);
            int i = 0;
            if (arrLength > 0)
            {
                for (; i < arrLength; ++i)
                {
                    byte v = Hash[i];
                    sb.Append(hexChars[(v >> 4) & 0xf]);
                    sb.Append(hexChars[(v >> 0) & 0xf]);
                }
            }

            if (!isEven)
            {
                byte v = Hash[i];
                sb.Append(hexChars[(v >> 4) & 0xf]);
            }

            return sb.ToString();
        }

        public static explicit operator byte[](SHA1Hash value)
        {
            return value.Hash;
        }

        public static explicit operator SHA1Hash(byte[] value)
        {
            return new SHA1Hash(value);
        }
    }
}
