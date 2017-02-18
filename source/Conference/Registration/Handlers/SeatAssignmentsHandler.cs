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

using AutoMapper;
using Infrastructure.EventSourcing;
using Infrastructure.Messaging.Handling;
using Registration.Commands;
using Registration.Events;

namespace Registration.Handlers
{
    public class SeatAssignmentsHandler :
        IEventHandler<OrderConfirmed>,
        IEventHandler<OrderPaymentConfirmed>,
        ICommandHandler<UnassignSeat>,
        ICommandHandler<AssignSeat>
    {
        private readonly IEventSourcedRepository<SeatAssignments> assignmentsRepo;

        private readonly IEventSourcedRepository<Order> ordersRepo;

        static SeatAssignmentsHandler()
        {
            // Mapping old version of the OrderPaymentConfirmed event to the new version.
            // Currently it is being done explicitly by the consumer, but this one in particular could be done
            // at the deserialization level, as it is just a rename, not a functionality change.
            Mapper.CreateMap<OrderPaymentConfirmed, OrderConfirmed>();
        }

        public SeatAssignmentsHandler(IEventSourcedRepository<Order> ordersRepo, IEventSourcedRepository<SeatAssignments> assignmentsRepo)
        {
            this.ordersRepo = ordersRepo;
            this.assignmentsRepo = assignmentsRepo;
        }

        public void Handle(AssignSeat command)
        {
            var assignments = assignmentsRepo.Get(command.SeatAssignmentsId);
            assignments.AssignSeat(command.Position, command.Attendee);
            assignmentsRepo.Save(assignments, command.Id.ToString());
        }

        public void Handle(UnassignSeat command)
        {
            var assignments = assignmentsRepo.Get(command.SeatAssignmentsId);
            assignments.Unassign(command.Position);
            assignmentsRepo.Save(assignments, command.Id.ToString());
        }

        public void Handle(OrderConfirmed @event)
        {
            var order = ordersRepo.Get(@event.SourceId);
            var assignments = order.CreateSeatAssignments();
            assignmentsRepo.Save(assignments, null);
        }

        public void Handle(OrderPaymentConfirmed @event)
        {
            Handle(Mapper.Map<OrderConfirmed>(@event));
        }
    }
}