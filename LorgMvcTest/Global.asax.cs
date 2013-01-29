using Lorg.Mvc4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;

namespace LorgMvcTest
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801

    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            ExceptionLogger.CreateLogger(new Lorg.Logger.Configuration()
            {
                ApplicationName = "LorgMvcTest",
                EnvironmentName = "Local",
                ConnectionString = new System.Data.SqlClient.SqlConnectionStringBuilder()
                {
                    InitialCatalog = "Lorg",
                    DataSource = ".",
                    IntegratedSecurity = true,
                }.ToString()
            });

            AreaRegistration.RegisterAllAreas();

            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}