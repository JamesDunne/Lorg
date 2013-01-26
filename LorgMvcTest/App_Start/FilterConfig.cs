using System.Web;
using System.Web.Mvc;

namespace LorgMvcTest
{
    public static class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new LorgMvcTest.LogExceptionsAttribute());
            filters.Add(new HandleErrorAttribute());
        }
    }
}