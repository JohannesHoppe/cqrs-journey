﻿// ==============================================================================================================
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
using System.Linq;
using AutoMapper;
using Infrastructure.EventSourcing;
using Infrastructure.Utils;
using Registration.Events;

namespace Registration
{
    /// <summary>
    ///     Entity used to represent seats asignments.
    /// </summary>
    /// <remarks>
    ///     In our current business logic, 1 seats assignments instance corresponds to 1 <see cref="Order" /> instance.
    ///     This does not need to be the case in the future.
    ///     <para>
    ///         For more information on the domain, see
    ///         <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258553">Journey chapter 3</see>.
    ///     </para>
    /// </remarks>
    public class SeatAssignments : EventSourced
    {
        private Dictionary<int, SeatAssignment> seats = new Dictionary<int, SeatAssignment>();

        static SeatAssignments()
        {
            Mapper.Initialize(cfg => {
                cfg.CreateMap<SeatAssigned, SeatAssignment>();
                cfg.CreateMap<SeatUnassigned, SeatAssignment>();
                cfg.CreateMap<SeatAssignmentUpdated, SeatAssignment>();
            });
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SeatAssignments" /> class.
        /// </summary>
        /// <param name="orderId">The order id that triggers this seat assignments creation.</param>
        /// <param name="seats">The order seats.</param>
        public SeatAssignments(Guid orderId, IEnumerable<SeatQuantity> seats)
            // Note that we don't use the order id as the assignments id
            : this(GuidUtil.NewSequentialId())
        {
            // Add as many assignments as seats there are.
            var i = 0;
            var all = new List<SeatAssignmentsCreated.SeatAssignmentInfo>();
            foreach (var seatQuantity in seats) {
                for (var j = 0; j < seatQuantity.Quantity; j++) {
                    all.Add(new SeatAssignmentsCreated.SeatAssignmentInfo {Position = i++, SeatType = seatQuantity.SeatType});
                }
            }

            Update(new SeatAssignmentsCreated {OrderId = orderId, Seats = all});
        }

        public SeatAssignments(Guid id, IEnumerable<IVersionedEvent> history)
            : this(id)
        {
            LoadFrom(history);
        }

        private SeatAssignments(Guid id)
            : base(id)
        {
            Handles<SeatAssignmentsCreated>(OnCreated);
            Handles<SeatAssigned>(OnSeatAssigned);
            Handles<SeatUnassigned>(OnSeatUnassigned);
            Handles<SeatAssignmentUpdated>(OnSeatAssignmentUpdated);
        }

        public void AssignSeat(int position, PersonalInfo attendee)
        {
            if (string.IsNullOrEmpty(attendee.Email)) {
                throw new ArgumentNullException("attendee.Email");
            }

            SeatAssignment current;
            if (!seats.TryGetValue(position, out current)) {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (!attendee.Email.Equals(current.Attendee.Email, StringComparison.InvariantCultureIgnoreCase)) {
                if (current.Attendee.Email != null) {
                    Update(new SeatUnassigned(Id) {Position = position});
                }

                Update(new SeatAssigned(Id) {
                    Position = position,
                    SeatType = current.SeatType,
                    Attendee = attendee
                });
            } else if (!string.Equals(attendee.FirstName, current.Attendee.FirstName, StringComparison.InvariantCultureIgnoreCase)
                || !string.Equals(attendee.LastName, current.Attendee.LastName, StringComparison.InvariantCultureIgnoreCase)) {
                Update(new SeatAssignmentUpdated(Id) {
                    Position = position,
                    Attendee = attendee
                });
            }
        }

        public void Unassign(int position)
        {
            SeatAssignment current;
            if (!seats.TryGetValue(position, out current)) {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (current.Attendee.Email != null) {
                Update(new SeatUnassigned(Id) {Position = position});
            }
        }

        private void OnCreated(SeatAssignmentsCreated e)
        {
            seats = e.Seats.ToDictionary(x => x.Position, x => new SeatAssignment {Position = x.Position, SeatType = x.SeatType});
        }

        private void OnSeatAssigned(SeatAssigned e)
        {
            seats[e.Position] = Mapper.Map(e, new SeatAssignment());
        }

        private void OnSeatUnassigned(SeatUnassigned e)
        {
            seats[e.Position] = Mapper.Map(e, new SeatAssignment {SeatType = seats[e.Position].SeatType});
        }

        private void OnSeatAssignmentUpdated(SeatAssignmentUpdated e)
        {
            seats[e.Position] = Mapper.Map(e, new SeatAssignment {
                // Seat type is also never received again from the client.
                SeatType = seats[e.Position].SeatType,
                // The email property is not received for updates, as those 
                // are for the same attendee essentially.
                Attendee = new PersonalInfo {Email = seats[e.Position].Attendee.Email}
            });
        }

        private class SeatAssignment
        {
            public int Position { get; set; }

            public Guid SeatType { get; set; }

            public PersonalInfo Attendee { get; set; }

            public SeatAssignment()
            {
                Attendee = new PersonalInfo();
            }
        }
    }
}