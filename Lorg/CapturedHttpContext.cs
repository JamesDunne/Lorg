using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public class CapturedHttpContext
    {
        internal readonly Uri Url;
        internal readonly Uri UrlReferrer;
        internal readonly System.Security.Principal.IPrincipal User;
        internal readonly string HttpMethod;

        public CapturedHttpContext(System.Web.HttpContextBase httpContext)
        {
            Url = httpContext.Request.Url;
            UrlReferrer = httpContext.Request.UrlReferrer;
            User = httpContext.User;
            HttpMethod = httpContext.Request.HttpMethod;
            Headers = new System.Collections.Specialized.NameValueCollection(httpContext.Request.Headers);
        }

        public CapturedHttpContext(System.Web.HttpContext httpContext)
        {
            Url = httpContext.Request.Url;
            UrlReferrer = httpContext.Request.UrlReferrer;
            User = httpContext.User;
            HttpMethod = httpContext.Request.HttpMethod;
            Headers = new System.Collections.Specialized.NameValueCollection(httpContext.Request.Headers);
        }

        public System.Collections.Specialized.NameValueCollection Headers { get; set; }
    }

}
