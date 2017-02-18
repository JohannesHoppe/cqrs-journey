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

using System;
using System.Collections.Specialized;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Moq;
using Moq.Protected;
using Registration.ReadModel;
using Xunit;

namespace Conference.Web.Public.Tests
{
    public class ConferenceTenantControllerFixture
    {
        private readonly Mock<IConferenceDao> dao;

        private readonly RouteData routeData;

        private readonly RouteCollection routes;

        private readonly TestController sut;

        public ConferenceTenantControllerFixture()
        {
            routes = new RouteCollection();

            routeData = new RouteData();
            routeData.Values.Add("controller", "Test");
            routeData.Values.Add("conferenceCode", "demo");

            var requestMock = new Mock<HttpRequestBase>(MockBehavior.Strict);
            requestMock.SetupGet(x => x.ApplicationPath).Returns("/");
            requestMock.SetupGet(x => x.Url).Returns(new Uri("http://localhost/request", UriKind.Absolute));
            requestMock.SetupGet(x => x.ServerVariables).Returns(new NameValueCollection());
            requestMock.Setup(x => x.ValidateInput());

            var responseMock = new Mock<HttpResponseBase>(MockBehavior.Strict);
            responseMock.Setup(x => x.ApplyAppPathModifier(It.IsAny<string>())).Returns<string>(s => s);

            var context = Mock.Of<HttpContextBase>(c => c.Request == requestMock.Object && c.Response == responseMock.Object);

            dao = new Mock<IConferenceDao>();

            sut = new TestController(dao.Object);
            sut.ControllerContext = new ControllerContext(context, routeData, sut);
            sut.Url = new UrlHelper(new RequestContext(context, routeData), routes);
        }

        [Fact]
        public void when_rendering_view_then_sets_conference_view_model()
        {
            var dto = new ConferenceAlias();
            dao.Setup(x => x.GetConferenceAlias("demo"))
                .Returns(dto);

            var invoker = new Mock<ControllerActionInvoker> {CallBase = true};
            invoker.Protected().Setup("InvokeActionResult", ItExpr.IsAny<ControllerContext>(), ItExpr.IsAny<ActionResult>());

            routeData.Values.Add("action", "Display");
            var result = invoker.Object.InvokeAction(sut.ControllerContext, "Display");

            Assert.True(result);
            Assert.NotNull((object) sut.ViewBag.Conference);
            Assert.Same(dto, sut.ViewBag.Conference);
        }

        [Fact]
        public void when_result_is_not_view_then_does_not_set_viewbag()
        {
            var invoker = new Mock<ControllerActionInvoker> {CallBase = true};
            invoker.Protected().Setup("InvokeActionResult", ItExpr.IsAny<ControllerContext>(), ItExpr.IsAny<ActionResult>());

            routeData.Values.Add("action", "Redirect");
            var result = invoker.Object.InvokeAction(sut.ControllerContext, "Redirect");

            Assert.True(result);
            Assert.Null((object) sut.ViewBag.Conference);
        }

        [Fact]
        public void when_invalid_conference_code_then_http_not_found()
        {
            var invoker = new Mock<ControllerActionInvoker> {CallBase = true};
            ActionResult result = null;
            invoker.Protected()
                .Setup("InvokeActionResult", ItExpr.IsAny<ControllerContext>(), ItExpr.IsAny<ActionResult>())
                .Callback<ControllerContext, ActionResult>((c, r) => result = r);

            // No setup for retrieving a conference DTO.
            routeData.Values.Add("action", "Display");
            invoker.Object.InvokeAction(sut.ControllerContext, "Display");

            Assert.NotNull(result);
            Assert.IsType<HttpNotFoundResult>(result);
        }

        public class TestController : ConferenceTenantController
        {
            public TestController(IConferenceDao dao)
                : base(dao) { }

            public ActionResult Display()
            {
                return View();
            }

            public ActionResult Redirect()
            {
                return Redirect("contoso.com");
            }
        }
    }
}