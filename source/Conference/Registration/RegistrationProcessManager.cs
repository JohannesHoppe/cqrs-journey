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
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using Infrastructure.Messaging;
using Infrastructure.Processes;
using Infrastructure.Utils;
using Payments.Contracts.Events;
using Registration.Commands;
using Registration.Events;

namespace Registration
{
    /// <summary>
    ///     Represents a Process Manager that is in charge of communicating between the different distributed components
    ///     when registering to a conference, reserving the seats, expiring the reservation in case the order is not
    ///     completed in time, etc.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For more information on the domain, see
    ///         <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258553">Journey chapter 3</see>.
    ///     </para>
    ///     <para>
    ///         For more information on Process Managers, see
    ///         <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258564">Reference 6</see>.
    ///     </para>
    ///     <para>
    ///         For more information on the optimizations and hardening we did to this class, and for more potential
    ///         performance and scalability optimizations,
    ///         see <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258557"> Journey chapter 7</see>.
    ///     </para>
    ///     <para>
    ///         There are a few things that we learnt along the way regarding Process Managers, which we might do differently
    ///         with the new insights that we
    ///         now have. See <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258558"> Journey lessons learnt</see> for
    ///         more information.
    ///     </para>
    /// </remarks>
    public class RegistrationProcessManager : IProcessManager
    {
        private static readonly TimeSpan BufferTimeBeforeReleasingSeatsAfterExpiration = TimeSpan.FromMinutes(14);

        public enum ProcessState
        {
            NotStarted = 0,

            AwaitingReservationConfirmation = 1,

            ReservationConfirmationReceived = 2,

            PaymentConfirmationReceived = 3
        }

        private readonly List<Envelope<ICommand>> commands = new List<Envelope<ICommand>>();

        public Guid ConferenceId { get; set; }

        public Guid OrderId { get; internal set; }

        public Guid ReservationId { get; internal set; }

        public Guid SeatReservationCommandId { get; internal set; }

        // feels awkward and possibly disrupting to store these properties here. Would it be better if instead of using 
        // current state values, we use event sourcing?
        public DateTime? ReservationAutoExpiration { get; internal set; }

        public Guid ExpirationCommandId { get; set; }

        public ProcessState State { get; internal set; }

        [ConcurrencyCheck]
        [Timestamp]
        public byte[] TimeStamp { get; private set; }

        public RegistrationProcessManager()
        {
            Id = GuidUtil.NewSequentialId();
        }

        public void Handle(OrderPlaced message)
        {
            if (State == ProcessState.NotStarted) {
                ConferenceId = message.ConferenceId;
                OrderId = message.SourceId;
                // Use the order id as an opaque reservation id for the seat reservation. 
                // It could be anything else, as long as it is deterministic from the OrderPlaced event.
                ReservationId = message.SourceId;
                ReservationAutoExpiration = message.ReservationAutoExpiration;
                var expirationWindow = message.ReservationAutoExpiration.Subtract(DateTime.UtcNow);

                if (expirationWindow > TimeSpan.Zero) {
                    State = ProcessState.AwaitingReservationConfirmation;
                    var seatReservationCommand =
                        new MakeSeatReservation {
                            ConferenceId = ConferenceId,
                            ReservationId = ReservationId,
                            Seats = message.Seats.ToList()
                        };
                    SeatReservationCommandId = seatReservationCommand.Id;

                    AddCommand(new Envelope<ICommand>(seatReservationCommand) {
                        TimeToLive = expirationWindow.Add(TimeSpan.FromMinutes(1))
                    });

                    var expirationCommand = new ExpireRegistrationProcess {ProcessId = Id};
                    ExpirationCommandId = expirationCommand.Id;
                    AddCommand(new Envelope<ICommand>(expirationCommand) {
                        Delay = expirationWindow.Add(BufferTimeBeforeReleasingSeatsAfterExpiration)
                    });
                } else {
                    AddCommand(new RejectOrder {OrderId = OrderId});
                    Completed = true;
                }
            } else {
                if (message.ConferenceId != ConferenceId) {
                    // throw only if not reprocessing
                    throw new InvalidOperationException();
                }
            }
        }

        public void Handle(OrderUpdated message)
        {
            if (State == ProcessState.AwaitingReservationConfirmation
                || State == ProcessState.ReservationConfirmationReceived) {
                State = ProcessState.AwaitingReservationConfirmation;

                var seatReservationCommand =
                    new MakeSeatReservation {
                        ConferenceId = ConferenceId,
                        ReservationId = ReservationId,
                        Seats = message.Seats.ToList()
                    };
                SeatReservationCommandId = seatReservationCommand.Id;
                AddCommand(seatReservationCommand);
            } else {
                throw new InvalidOperationException("The order cannot be updated at this stage.");
            }
        }

        public void Handle(Envelope<SeatsReserved> envelope)
        {
            if (State == ProcessState.AwaitingReservationConfirmation) {
                if (envelope.CorrelationId != null) {
                    if (string.CompareOrdinal(SeatReservationCommandId.ToString(), envelope.CorrelationId) != 0) {
                        // skip this event
                        Trace.TraceWarning("Seat reservation response for reservation id {0} does not match the expected correlation id.", envelope.Body.ReservationId);
                        return;
                    }
                }

                State = ProcessState.ReservationConfirmationReceived;

                AddCommand(new MarkSeatsAsReserved {
                    OrderId = OrderId,
                    Seats = envelope.Body.ReservationDetails.ToList(),
                    Expiration = ReservationAutoExpiration.Value
                });
            } else if (string.CompareOrdinal(SeatReservationCommandId.ToString(), envelope.CorrelationId) == 0) {
                Trace.TraceInformation("Seat reservation response for request {1} for reservation id {0} was already handled. Skipping event.", envelope.Body.ReservationId, envelope.CorrelationId);
            } else {
                throw new InvalidOperationException("Cannot handle seat reservation at this stage.");
            }
        }

        public void Handle(PaymentCompleted @event)
        {
            if (State == ProcessState.ReservationConfirmationReceived) {
                State = ProcessState.PaymentConfirmationReceived;
                AddCommand(new ConfirmOrder {OrderId = OrderId});
            } else {
                throw new InvalidOperationException("Cannot handle payment confirmation at this stage.");
            }
        }

        public void Handle(OrderConfirmed @event)
        {
            if (State == ProcessState.ReservationConfirmationReceived || State == ProcessState.PaymentConfirmationReceived) {
                ExpirationCommandId = Guid.Empty;
                Completed = true;

                AddCommand(new CommitSeatReservation {
                    ReservationId = ReservationId,
                    ConferenceId = ConferenceId
                });
            } else {
                throw new InvalidOperationException("Cannot handle order confirmation at this stage.");
            }
        }

        public void Handle(ExpireRegistrationProcess command)
        {
            if (ExpirationCommandId == command.Id) {
                Completed = true;

                AddCommand(new RejectOrder {OrderId = OrderId});
                AddCommand(new CancelSeatReservation {
                    ConferenceId = ConferenceId,
                    ReservationId = ReservationId
                });

                // TODO cancel payment if any
            }

            // else ignore the message as it is no longer relevant (but not invalid)
        }

        private void AddCommand<T>(T command)
            where T : ICommand
        {
            commands.Add(Envelope.Create<ICommand>(command));
        }

        private void AddCommand(Envelope<ICommand> envelope)
        {
            commands.Add(envelope);
        }

        public Guid Id { get; }

        public bool Completed { get; private set; }

        public IEnumerable<Envelope<ICommand>> Commands {
            get { return commands; }
        }
    }
}