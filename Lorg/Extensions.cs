using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System
{
    public static class StringExtensions
    {
        public static string F(this string format, params object[] args)
        {
            return String.Format(format, args);
        }
    }

    public static class ByteArrayExtensions
    {
        const string hexChars = "0123456789abcdef";

        public static string ToHexString(this byte[] array, int length = -1)
        {
            if (length == 0) return String.Empty;
            if (length < 0) length = array.Length * 2;

            bool isEven = (length & 1) == 0;
            int arrLength = length / 2;

            var sb = new StringBuilder(length);
            int i = 0;
            if (arrLength > 0)
            {
                for (; i < arrLength; ++i)
                {
                    byte v = array[i];
                    sb.Append(hexChars[(v >> 4) & 0xf]);
                    sb.Append(hexChars[(v >> 0) & 0xf]);
                }
            }

            if (!isEven)
            {
                byte v = array[i];
                sb.Append(hexChars[(v >> 4) & 0xf]);
            }

            return sb.ToString();
        }
    }
}

namespace System.Data.SqlClient
{
    public static class SqlParameterCollectionExtensions
    {
        public static SqlParameterCollection AddOutParam(this SqlParameterCollection prms, string name, SqlDbType type)
        {
            var prm = prms.Add(name, type);
            prm.Direction = ParameterDirection.Output;
            return prms;
        }

        public static SqlParameterCollection AddOutParamSize(this SqlParameterCollection prms, string name, SqlDbType type, int size)
        {
            var prm = prms.Add(name, type, size);
            prm.Direction = ParameterDirection.Output;
            return prms;
        }

        public static SqlParameterCollection AddInParam(this SqlParameterCollection prms, string name, SqlDbType type, object value)
        {
            var prm = prms.Add(name, type);
            prm.Value = value == null ? (object)DBNull.Value : value;
            return prms;
        }

        public static SqlParameterCollection AddInParamSize(this SqlParameterCollection prms, string name, SqlDbType type, int size, object value)
        {
            var prm = prms.Add(name, type, size);
            prm.Value = value == null ? (object)DBNull.Value : value;
            return prms;
        }

        public static SqlParameterCollection AddInParamSHA1(this SqlParameterCollection prms, string name, byte[] hashValue)
        {
            var prm = prms.Add(name, SqlDbType.Binary, 20);
            prm.Value = hashValue == null ? (object)DBNull.Value : hashValue;
            return prms;
        }
    }
}