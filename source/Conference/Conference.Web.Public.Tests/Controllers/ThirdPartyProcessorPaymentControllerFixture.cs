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
using Conference.Web.Public.Areas.ThirdPartyProcessor.Controllers;
using Xunit;

namespace Conference.Web.Public.Tests.Controllers.ThirdPartyProcessorPaymentControllerFixture
{
    public class given_controller
    {
        protected readonly ThirdPartyProcessorPaymentController sut;

        public given_controller()
        {
            sut = new ThirdPartyProcessorPaymentController();
        }

        [Fact]
        public void when_intiating_payment_then_returns_payment_view()
        {
            var result = (ViewResult) sut.Pay("item", 100, "return", "cancelreturn");

            Assert.Equal(sut.ViewBag.ReturnUrl, "return");
            Assert.Equal(sut.ViewBag.CancelReturnUrl, "cancelreturn");
            Assert.Equal(sut.ViewBag.ItemName, "item");
            Assert.Equal(sut.ViewBag.ItemAmount, 100m);
        }

        [Fact]
        public void when_accepting_payment_then_redirects_to_return_url()
        {
            var result = (RedirectResult) sut.Pay("accepted", "return", "cancelReturn");

            Assert.Equal("return", result.Url);
            Assert.False(result.Permanent);
        }

        [Fact]
        public void when_rejecting_payment_then_redirects_to_cancel_return_url()
        {
            var result = (RedirectResult) sut.Pay("rejected", "return", "cancelReturn");

            Assert.Equal("cancelReturn", result.Url);
            Assert.False(result.Permanent);
        }
    }
}