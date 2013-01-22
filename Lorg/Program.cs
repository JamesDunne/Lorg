using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    class Program
    {
        static readonly Logger log = Logger.AttemptInitialize(
            new Logger.Configuration
            {
                ConnectionString = new SqlConnectionStringBuilder()
                {
                    InitialCatalog = "Lorg",
                    DataSource = ".",
                    AsynchronousProcessing = true,
                    IntegratedSecurity = true,
                }.ToString(),
                ApplicationName = "Test",
                EnvironmentName = "Local",
            }
        );

        static void DoTest(Action a)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; ++i)
            {
                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    log.Write(ex);
                }
            }
            sw.Stop();
            Console.WriteLine("{0} ms", sw.ElapsedMilliseconds);
        }

        static void Main(string[] args)
        {
            DoTest(Test1);
            DoTest(Test2);
        }

        static void Test1()
        {
            throw new Exception("Test1");
        }

        static void Test2()
        {
            throw new Exception("Test2");
        }
    }
}
