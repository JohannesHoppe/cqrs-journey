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

using System.Web.Mvc;
using Registration.ReadModel;

namespace Conference.Web.Public
{
    public abstract class ConferenceTenantController : AsyncController
    {
        private ConferenceAlias conferenceAlias;

        private string conferenceCode;

        public IConferenceDao ConferenceDao { get; }

        public string ConferenceCode {
            get {
                return conferenceCode ??
                    (conferenceCode = (string) ControllerContext.RouteData.Values["conferenceCode"]);
            }
            internal set { conferenceCode = value; }
        }

        public ConferenceAlias ConferenceAlias {
            get {
                return conferenceAlias ??
                    (conferenceAlias = ConferenceDao.GetConferenceAlias(ConferenceCode));
            }
            internal set { conferenceAlias = value; }
        }

        protected ConferenceTenantController(IConferenceDao conferenceDao)
        {
            ConferenceDao = conferenceDao;
        }

        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            base.OnActionExecuting(filterContext);

            if (!string.IsNullOrEmpty(ConferenceCode) &&
                ConferenceAlias == null) {
                filterContext.Result = new HttpNotFoundResult("Invalid conference code.");
            }
        }

        protected override void OnResultExecuting(ResultExecutingContext filterContext)
        {
            base.OnResultExecuting(filterContext);

            if (filterContext.Result is ViewResultBase) {
                ViewBag.Conference = ConferenceAlias;
            }
        }
    }
}