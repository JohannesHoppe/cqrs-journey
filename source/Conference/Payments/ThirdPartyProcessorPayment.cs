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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Infrastructure.Database;
using Infrastructure.Messaging;
using Payments.Contracts.Events;

namespace Payments
{
    /// <summary>
    ///     Represents a payment through a 3rd party system.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For more information on the Payments BC, see
    ///         <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258555">Journey chapter 5</see>.
    ///     </para>
    /// </remarks>
    public class ThirdPartyProcessorPayment : IAggregateRoot, IEventPublisher
    {
        public enum States
        {
            Initiated = 0,
            Accepted = 1,
            Completed = 2,
            Rejected = 3
        }

        private readonly List<IEvent> events = new List<IEvent>();

        public States State { get; internal set; }

        public Guid PaymentSourceId { get; }

        public string Description { get; }

        public decimal TotalAmount { get; }

        public virtual ICollection<ThidPartyProcessorPaymentItem> Items { get; }

        public ThirdPartyProcessorPayment(Guid id, Guid paymentSourceId, string description, decimal totalAmount, IEnumerable<ThidPartyProcessorPaymentItem> items)
            : this()
        {
            Id = id;
            PaymentSourceId = paymentSourceId;
            Description = description;
            TotalAmount = totalAmount;
            Items.AddRange(items);

            AddEvent(new PaymentInitiated {SourceId = id, PaymentSourceId = paymentSourceId});
        }

        protected ThirdPartyProcessorPayment()
        {
            Items = new ObservableCollection<ThidPartyProcessorPaymentItem>();
        }

        public void Complete()
        {
            if (State != States.Initiated) {
                throw new InvalidOperationException();
            }

            State = States.Completed;
            AddEvent(new PaymentCompleted {SourceId = Id, PaymentSourceId = PaymentSourceId});
        }

        public void Cancel()
        {
            if (State != States.Initiated) {
                throw new InvalidOperationException();
            }

            State = States.Rejected;
            AddEvent(new PaymentRejected {SourceId = Id, PaymentSourceId = PaymentSourceId});
        }

        protected void AddEvent(IEvent @event)
        {
            events.Add(@event);
        }

        public Guid Id { get; }

        public IEnumerable<IEvent> Events => events;
    }
}