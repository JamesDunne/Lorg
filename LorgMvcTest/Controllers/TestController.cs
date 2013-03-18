using Lorg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace LorgMvcTest.Controllers
{
    public class TestController : Controller
    {
#pragma warning disable 1998
        async Task<ActionResult> Test(int count, Func<Task> attempt)
        {
            var correlationID = Guid.NewGuid();
            var sb = new StringBuilder(count * (20 + 2));

            var tasks = new Task<ILogIdentifier>[count];

            for (int i = 0; i < count; ++i)
            {
                tasks[i] = this.Try(attempt, isHandled: true, correlationID: correlationID);
            }

            await Task.WhenAll(tasks);
            for (int i = 0; i < count; ++i)
            {
                var logid = tasks[i].Result;
                if (logid != null)
                    sb.AppendLine(logid.GetEndUserFormat());
            }

            return Content(sb.ToString(), "text/plain");
        }

        public async Task<ActionResult> Index()
        {
            return await Test(100, async () => { throw new NullReferenceException("Test"); });
        }

        public async Task<ActionResult> Bulk()
        {
            return await Test(1000, async () => { throw new Exception("Test"); });
        }

        public async Task<ActionResult> Inner()
        {
            return await Test(1, async () => { throw new Exception("Test; 1st level", new Exception("Inner test; 2nd level", new System.IO.FileNotFoundException("Could not find example file; 3rd level", new Exception("4th level")))); });
        }

        public async Task<JsonResult> Json()
        {
            throw new NotImplementedException();
        }
#pragma warning restore 1998
    }
}
