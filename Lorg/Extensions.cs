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

        public static SqlParameterCollection AddInParamSHA1(this SqlParameterCollection prms, string name, Lorg.SHA1Hash? hashValue)
        {
            var prm = prms.Add(name, SqlDbType.Binary, 20);
            prm.Value = hashValue.HasValue ? hashValue.Value.Hash : (object)DBNull.Value;
            return prms;
        }
    }
}