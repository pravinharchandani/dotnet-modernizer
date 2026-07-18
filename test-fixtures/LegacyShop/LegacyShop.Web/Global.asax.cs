using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace LegacyShop.Web
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            Exception ex = Server.GetLastError();
            System.Diagnostics.Trace.TraceError("Unhandled exception: {0}", ex);
        }
    }
}
