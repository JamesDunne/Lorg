using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public sealed class SqlConnectionContext
    {
        readonly SqlConnection conn;
        readonly SqlTransaction tran;

        public SqlConnectionContext(SqlConnection conn, SqlTransaction tran = null)
        {
            this.conn = conn;
            this.tran = tran;
        }

        SqlCommand CreateCommand()
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tran;
            return cmd;
        }

        public async Task<TResult> ExecReader<TResult>(
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlDataReader, SqlCommand, Task<TResult>> read
        )
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr, cmd);
            }
        }

        public async Task<TResult> ExecReader<TResult>(
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlDataReader, Task<TResult>> read
        )
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr);
            }
        }

        public async Task<TResult> ExecReader<TResult>(
            string text,
            Func<SqlDataReader, Task<TResult>> read
        )
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr);
            }
        }

        public async Task<int> ExecNonQuery(
            string text
        )
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<int> ExecNonQuery(
            string text,
            Action<SqlParameterCollection> bindParameters
        )
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<TResult> ExecNonQuery<TResult>(
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlParameterCollection, int, TResult> report
        )
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                int rc = await cmd.ExecuteNonQueryAsync();
                return report(cmd.Parameters, rc);
            }
        }
    }
}
