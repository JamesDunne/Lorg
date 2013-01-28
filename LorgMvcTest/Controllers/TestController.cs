using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace LorgMvcTest.Controllers
{
    public class TestController : Controller
    {
        //
        // GET: /Test/

        public async Task<ActionResult> Index()
        {
            for (int i = 0; i < 100; ++i)
                await ExceptionLogger.HandleExceptions(async () => { throw new NullReferenceException("Test"); });
            return Content("");
        }

        public async Task<ActionResult> Bulk()
        {
            for (int i = 0; i < 1000; ++i)
                await ExceptionLogger.HandleExceptions(async () => { throw new Exception("Test"); });
            return Content("");
        }

        public async Task<ActionResult> Inner()
        {
            for (int i = 0; i < 500; ++i)
                await ExceptionLogger.HandleExceptions(async () => { throw new Exception("Test", new Exception("Inner test")); });
            return Content("");
        }

        public async Task<JsonResult> Json()
        {
            throw new NotImplementedException();
        }
    }
}
