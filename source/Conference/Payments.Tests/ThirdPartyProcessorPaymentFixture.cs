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
using System.Linq;
using Payments.Contracts.Events;
using Xunit;

namespace Payments.Tests.ThirdPartyProcessorPaymentFixture
{
    public class given_no_payment
    {
        private static readonly Guid PaymentId = Guid.NewGuid();

        private static readonly Guid SourceId = Guid.NewGuid();

        private static readonly Guid ConferenceId = Guid.NewGuid();

        private ThirdPartyProcessorPayment sut;

        private IPersistenceProvider sutProvider;

        protected given_no_payment(IPersistenceProvider sutProvider)
        {
            this.sutProvider = sutProvider;
        }

        public given_no_payment()
            : this(new NoPersistenceProvider()) { }

        [Fact]
        public void when_initiating_payment_then_status_is_initiated()
        {
            InitiatePayment();

            Assert.Equal(PaymentId, sut.Id);
            Assert.Equal(ThirdPartyProcessorPayment.States.Initiated, sut.State);
        }

        [Fact]
        public void when_initiating_payment_then_raises_integration_event()
        {
            InitiatePayment();

            Assert.Single(sut.Events);
            Assert.Equal(PaymentId, ((PaymentInitiated) sut.Events.Single()).SourceId);
            Assert.Equal(SourceId, ((PaymentInitiated) sut.Events.Single()).PaymentSourceId);
        }

        private void InitiatePayment()
        {
            sut = new ThirdPartyProcessorPayment(PaymentId, SourceId, "payment", 300, new[] {new ThidPartyProcessorPaymentItem("item1", 100), new ThidPartyProcessorPaymentItem("item2", 200)});
        }
    }

    public class given_initated_payment
    {
        private static readonly Guid PaymentId = Guid.NewGuid();

        private static readonly Guid SourceId = Guid.NewGuid();

        private static readonly Guid ConferenceId = Guid.NewGuid();

        private readonly ThirdPartyProcessorPayment sut;

        private readonly IPersistenceProvider sutProvider;

        protected given_initated_payment(IPersistenceProvider sutProvider)
        {
            this.sutProvider = sutProvider;

            sut = new ThirdPartyProcessorPayment(PaymentId, SourceId, "payment", 300, new[] {new ThidPartyProcessorPaymentItem("item1", 100), new ThidPartyProcessorPaymentItem("item2", 200)});

            sut = this.sutProvider.PersistReload(sut);
        }

        public given_initated_payment()
            : this(new NoPersistenceProvider()) { }

        [Fact]
        public void when_completing_payment_then_changes_status()
        {
            sut.Complete();

            Assert.Equal(ThirdPartyProcessorPayment.States.Completed, sut.State);
        }

        [Fact]
        public void when_completing_payment_then_notifies_event()
        {
            sut.Complete();

            var @event = (PaymentCompleted) sut.Events.Last();
            Assert.Equal(PaymentId, @event.SourceId);
            Assert.Equal(SourceId, @event.PaymentSourceId);
        }

        [Fact]
        public void when_rejecting_payment_then_changes_status()
        {
            sut.Complete();

            Assert.Equal(ThirdPartyProcessorPayment.States.Completed, sut.State);
        }

        [Fact]
        public void when_rejecting_payment_then_notifies_event()
        {
            sut.Cancel();

            var @event = (PaymentRejected) sut.Events.Last();
            Assert.Equal(PaymentId, @event.SourceId);
            Assert.Equal(SourceId, @event.PaymentSourceId);
        }
    }
}