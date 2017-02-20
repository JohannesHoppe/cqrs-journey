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
using System.ComponentModel.DataAnnotations;

namespace Registration.ReadModel
{
    public class DraftOrder
    {
        public enum States
        {
            PendingReservation = 0,
            PartiallyReserved = 1,
            ReservationCompleted = 2,
            Rejected = 3,
            Confirmed = 4
        }

        [Key]
        public Guid OrderId { get; }

        public Guid ConferenceId { get; }

        public DateTime? ReservationExpirationDate { get; set; }

        public ICollection<DraftOrderItem> Lines { get; }

        public States State { get; set; }

        public int OrderVersion { get; internal set; }

        public string RegistrantEmail { get; internal set; }

        public string AccessCode { get; internal set; }

        public DraftOrder(Guid orderId, Guid conferenceId, States state, int orderVersion = 0)
            : this()
        {
            OrderId = orderId;
            ConferenceId = conferenceId;
            State = state;
            OrderVersion = orderVersion;
        }

        protected DraftOrder()
        {
            Lines = new ObservableCollection<DraftOrderItem>();
        }
    }
}