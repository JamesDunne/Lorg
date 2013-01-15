using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    class Program
    {
        static readonly Logger log = new Logger();

        static Program()
        {
            Logger.ValidConfiguration cfg;
            try
            {
                cfg = Logger.ValidateConfiguration(
                    new Logger.Configuration
                    {
                        ConnectionString = new SqlConnectionStringBuilder()
                        {
                            InitialCatalog = "Lorg",
                            DataSource = ".",
                            AsynchronousProcessing = true
                        }.ToString()
                    }
                );

                log.Initialize(cfg).Wait();
            }
            catch (Exception ex)
            {
                Logger.FailoverWrite(ex).Wait();
            }
        }

        static void DoTest(Action a)
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
