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
    public static class SqlConnectionExtensions
    {
        public static async Task<TResult> ExecReader<TResult>(
            this SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlDataReader, SqlCommand, Task<TResult>> read
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr, cmd);
            }
        }

        public static async Task<TResult> ExecReader<TResult>(
            this SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlDataReader, Task<TResult>> read
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr);
            }
        }

        public static async Task<TResult> ExecReader<TResult>(
            this SqlConnection conn,
            string text,
            Func<SqlDataReader, Task<TResult>> read
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr);
            }
        }

        public static async Task<int> ExecNonQuery(
            this SqlConnection conn,
            string text
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        public static async Task<int> ExecNonQuery(
            this SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        public static async Task<TResult> ExecNonQuery<TResult>(
            this SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlParameterCollection, int, TResult> report
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                int rc = await cmd.ExecuteNonQueryAsync();
                return report(cmd.Parameters, rc);
            }
        }
    }

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