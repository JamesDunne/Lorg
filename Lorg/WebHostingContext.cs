using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public class WebHostingContext
    {
        public string MachineName { get; private set; }
        public string ApplicationID { get; private set; }
        public string PhysicalPath { get; private set; }
        public string VirtualPath { get; private set; }
        public string SiteName { get; private set; }

        /// <summary>
        /// Initializes the WebHostingContext with values pulled from `System.Web.Hosting.HostingEnvironment`.
        /// </summary>
        public WebHostingContext()
        {
            MachineName = Environment.MachineName;
            ApplicationID = System.Web.Hosting.HostingEnvironment.ApplicationID;
            PhysicalPath = System.Web.Hosting.HostingEnvironment.ApplicationPhysicalPath;
            VirtualPath = System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath;
            SiteName = System.Web.Hosting.HostingEnvironment.SiteName;
        }
    }
}
