// ==============================================================================================================
// Microsoft patterns & practices
// CQRS Journey project
// ==============================================================================================================
// ©2012 Microsoft. All rights reserved. Certain content used with permission from contributors
// http://go.microsoft.com/fwlink/p/?LinkID=258575
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance 
// with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is 
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and limitations under the License.
// ==============================================================================================================

using System.Data.Entity;
using System.Diagnostics;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Conference.Common;
using Conference.Web.Utils;
using Infrastructure.Messaging;
using Infrastructure.Serialization;
using Infrastructure.Sql.Messaging;
using Infrastructure.Sql.Messaging.Implementation;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace Conference.Web.Admin
{
#if LOCAL
#else
    using Infrastructure.Azure.Messaging;
    using Infrastructure.Azure;
#endif

    public class MvcApplication : HttpApplication
    {
        public static IEventBus EventBus { get; private set; }

        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new MaintenanceModeAttribute());
            filters.Add(new HandleErrorAttribute());
        }

        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                "Conference.Locate",
                "locate",
                new {controller = "Conference", action = "Locate"}
            );

            routes.MapRoute(
                "Conference.Create",
                "create",
                new {controller = "Conference", action = "Create"}
            );

            routes.MapRoute(
                "Conference",
                "{slug}/{accessCode}/{action}",
                new {controller = "Conference", action = "Index"}
            );

            routes.MapRoute(
                "Home",
                "",
                new {controller = "Home", action = "Index"}
            );
        }

        protected void Application_Start()
        {
#if AZURESDK
            RoleEnvironment.Changed +=
                (s, a) => { RoleEnvironment.RequestRecycle(); };
#endif
            MaintenanceMode.RefreshIsInMaintainanceMode();

            DatabaseSetup.Initialize();

            AreaRegistration.RegisterAllAreas();

            RegisterGlobalFilters(GlobalFilters.Filters);
            RegisterRoutes(RouteTable.Routes);

            var serializer = new JsonTextSerializer();
#if LOCAL
            EventBus = new EventBus(new MessageSender(Database.DefaultConnectionFactory, "SqlBus", "SqlBus.Events"), serializer);
#else
            var settings = InfrastructureSettings.Read(HttpContext.Current.Server.MapPath(@"~\bin\Settings.xml")).ServiceBus;

            if (!MaintenanceMode.IsInMaintainanceMode)
            {
            new ServiceBusConfig(settings).Initialize();
            }

            EventBus = new EventBus(new TopicSender(settings, "conference/events"), new StandardMetadataProvider(), serializer);
#endif

#if AZURESDK
            if (RoleEnvironment.IsAvailable) {
                Trace.Listeners.Add(new DiagnosticMonitorTraceListener());
                Trace.AutoFlush = true;
            }
#endif
        }
    }
}