using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lorg;

namespace LorgTest
{
    class Program
    {
        /// <summary>
        /// Default exception logger
        /// </summary>
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

        /// <summary>
        /// Main program
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            DoTest(Test1).Wait();
            DoTest(Test2).Wait();
        }

        static async Task DoTest(Func<Task> a)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; ++i)
            {
                await log.HandleExceptions(a);
            }
            sw.Stop();
            Console.WriteLine("{0} ms", sw.ElapsedMilliseconds);
        }

        static async Task Test1()
        {
            throw new Exception("Test1");
        }

        static async Task Test2()
        {
            throw new Exception("Test2");
        }
    }
}
