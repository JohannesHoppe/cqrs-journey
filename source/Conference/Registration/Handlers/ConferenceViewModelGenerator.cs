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
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Conference;
using Infrastructure.EventSourcing;
using Infrastructure.Messaging;
using Infrastructure.Messaging.Handling;
using Registration.Commands;
using Registration.Events;
using Registration.ReadModel;
using Registration.ReadModel.Implementation;

namespace Registration.Handlers
{
    /// <summary>
    ///     Generates a read model that is queried by <see cref="ConferenceDao" />.
    /// </summary>
    public class ConferenceViewModelGenerator :
        IEventHandler<ConferenceCreated>,
        IEventHandler<ConferenceUpdated>,
        IEventHandler<ConferencePublished>,
        IEventHandler<ConferenceUnpublished>,
        IEventHandler<SeatCreated>,
        IEventHandler<SeatUpdated>,
        IEventHandler<AvailableSeatsChanged>,
        IEventHandler<SeatsReserved>,
        IEventHandler<SeatsReservationCancelled>
    {
        private readonly ICommandBus bus;

        private readonly Func<ConferenceRegistrationDbContext> contextFactory;

        public ConferenceViewModelGenerator(Func<ConferenceRegistrationDbContext> contextFactory, ICommandBus bus)
        {
            this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            this.bus = bus;
        }

        private void UpdateAvailableQuantity(IVersionedEvent @event, IEnumerable<SeatQuantity> seats)
        {
            using (var repository = contextFactory()) {
                var seatDtos = repository.Set<SeatType>().Where(x => x.ConferenceId == @event.SourceId).ToList();
                if (seatDtos.Count > 0) {
                    // This check assumes events might be received more than once, but not out of order
                    var maxSeatsAvailabilityVersion = seatDtos.Max(x => x.SeatsAvailabilityVersion);
                    if (maxSeatsAvailabilityVersion >= @event.Version) {
                        Trace.TraceWarning(
                            "Ignoring availability update message with version {1} for seat types with conference id {0}, last known version {2}.",
                            @event.SourceId,
                            @event.Version,
                            maxSeatsAvailabilityVersion);
                        return;
                    }

                    foreach (var seat in seats) {
                        var seatDto = seatDtos.FirstOrDefault(x => x.Id == seat.SeatType);
                        if (seatDto != null) {
                            seatDto.AvailableQuantity += seat.Quantity;
                            seatDto.SeatsAvailabilityVersion = @event.Version;
                        } else {
                            // TODO should reject the entire update?
                            Trace.TraceError(
                                "Failed to locate Seat Type read model being updated with id {0}.", seat.SeatType);
                        }
                    }

                    repository.SaveChanges();
                } else {
                    Trace.TraceError(
                        "Failed to locate Seat Types read model for updated seat availability, with conference id {0}.",
                        @event.SourceId);
                }
            }
        }

        public void Handle(AvailableSeatsChanged @event)
        {
            UpdateAvailableQuantity(@event, @event.Seats);
        }

        public void Handle(ConferenceCreated @event)
        {
            using (var repository = contextFactory.Invoke()) {
                var dto = repository.Find<ReadModel.Conference>(@event.SourceId);
                if (dto != null) {
                    Trace.TraceWarning(
                        "Ignoring ConferenceCreated event for conference with ID {0} as it was already created.",
                        @event.SourceId);
                } else {
                    repository.Set<ReadModel.Conference>()
                        .Add(
                            new ReadModel.Conference(
                                @event.SourceId,
                                @event.Slug,
                                @event.Name,
                                @event.Description,
                                @event.Location,
                                @event.Tagline,
                                @event.TwitterSearch,
                                @event.StartDate,
                                Enumerable.Empty<SeatType>()));

                    repository.SaveChanges();
                }
            }
        }

        public void Handle(ConferencePublished @event)
        {
            using (var repository = contextFactory.Invoke()) {
                var dto = repository.Find<ReadModel.Conference>(@event.SourceId);
                if (dto != null) {
                    dto.IsPublished = true;

                    repository.Save(dto);
                } else {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Failed to locate Conference read model for published conference with id {0}.", @event.SourceId));
                }
            }
        }

        public void Handle(ConferenceUnpublished @event)
        {
            using (var repository = contextFactory.Invoke()) {
                var dto = repository.Find<ReadModel.Conference>(@event.SourceId);
                if (dto != null) {
                    dto.IsPublished = false;

                    repository.Save(dto);
                } else {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Failed to locate Conference read model for unpublished conference with id {0}.", @event.SourceId));
                }
            }
        }

        public void Handle(ConferenceUpdated @event)
        {
            using (var repository = contextFactory.Invoke()) {
                var confDto = repository.Find<ReadModel.Conference>(@event.SourceId);
                if (confDto != null) {
                    confDto.Code = @event.Slug;
                    confDto.Description = @event.Description;
                    confDto.Location = @event.Location;
                    confDto.Name = @event.Name;
                    confDto.StartDate = @event.StartDate;
                    confDto.Tagline = @event.Tagline;
                    confDto.TwitterSearch = @event.TwitterSearch;

                    repository.SaveChanges();
                } else {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Failed to locate Conference read model for updated conference with id {0}.", @event.SourceId));
                }
            }
        }

        public void Handle(SeatCreated @event)
        {
            using (var repository = contextFactory.Invoke()) {
                var dto = repository.Find<SeatType>(@event.SourceId);
                if (dto != null) {
                    Trace.TraceWarning(
                        "Ignoring SeatCreated event for seat type with ID {0} as it was already created.",
                        @event.SourceId);
                } else {
                    dto = new SeatType(
                        @event.SourceId,
                        @event.ConferenceId,
                        @event.Name,
                        @event.Description,
                        @event.Price,
                        @event.Quantity);

                    bus.Send(
                        new AddSeats {
                            ConferenceId = @event.ConferenceId,
                            SeatType = @event.SourceId,
                            Quantity = @event.Quantity
                        });

                    repository.Save(dto);
                }
            }
        }

        public void Handle(SeatsReservationCancelled @event)
        {
            UpdateAvailableQuantity(@event, @event.AvailableSeatsChanged);
        }

        public void Handle(SeatsReserved @event)
        {
            UpdateAvailableQuantity(@event, @event.AvailableSeatsChanged);
        }

        public void Handle(SeatUpdated @event)
        {
            using (var repository = contextFactory.Invoke()) {
                var dto = repository.Find<SeatType>(@event.SourceId);
                if (dto != null) {
                    dto.Description = @event.Description;
                    dto.Name = @event.Name;
                    dto.Price = @event.Price;

                    // Calculate diff to drive the seat availability.
                    // Is it appropriate to have this here?
                    var diff = @event.Quantity - dto.Quantity;

                    dto.Quantity = @event.Quantity;

                    repository.Save(dto);

                    if (diff > 0) {
                        bus.Send(
                            new AddSeats {
                                ConferenceId = @event.ConferenceId,
                                SeatType = @event.SourceId,
                                Quantity = diff
                            });
                    } else {
                        bus.Send(
                            new RemoveSeats {
                                ConferenceId = @event.ConferenceId,
                                SeatType = @event.SourceId,
                                Quantity = Math.Abs(diff)
                            });
                    }
                } else {
                    throw new InvalidOperationException(
                        string.Format("Failed to locate Seat Type read model being updated with id {0}.", @event.SourceId));
                }
            }
        }
    }
}