using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public class WebHostingContext
    {
        public string ApplicationID { get; private set; }
        public string ApplicationPhysicalPath { get; private set; }
        public string ApplicationVirtualPath { get; private set; }
        public string SiteName { get; private set; }

        /// <summary>
        /// Initializes the WebHostingContext with values pulled from `System.Web.Hosting.HostingEnvironment`.
        /// </summary>
        public WebHostingContext()
        {
            ApplicationID = System.Web.Hosting.HostingEnvironment.ApplicationID;
            ApplicationPhysicalPath = System.Web.Hosting.HostingEnvironment.ApplicationPhysicalPath;
            ApplicationVirtualPath = System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath;
            SiteName = System.Web.Hosting.HostingEnvironment.SiteName;
        }
    }
}
