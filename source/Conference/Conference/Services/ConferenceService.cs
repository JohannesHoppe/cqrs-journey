﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Core;
using System.Linq;
using Infrastructure.Messaging;

namespace Conference
{
    /// <summary>
    ///     Transaction-script style domain service that manages
    ///     the interaction between the MVC controller and the
    ///     ORM persistence, as well as the publishing of integration
    ///     events.
    /// </summary>
    public class ConferenceService
    {
        private readonly IEventBus eventBus;

        public ConferenceService(IEventBus eventBus)
        {
            this.eventBus = eventBus;
        }

        public void CreateConference(ConferenceInfo conference)
        {
            using (var context = new ConferenceContext()) {
                var existingSlug =
                    context.Conferences
                        .Where(c => c.Slug == conference.Slug)
                        .Select(c => c.Slug)
                        .Any();

                if (existingSlug) {
                    throw new DuplicateNameException("The chosen conference slug is already taken.");
                }

                // Conference publishing is explicit. 
                if (conference.IsPublished) {
                    conference.IsPublished = false;
                }

                context.Conferences.Add(conference);
                context.SaveChanges();

                PublishConferenceEvent<ConferenceCreated>(conference);
            }
        }

        public void CreateSeat(Guid conferenceId, SeatType seat)
        {
            using (var context = new ConferenceContext()) {
                var conference = context.Conferences.Find(conferenceId);
                if (conference == null) {
                    throw new ObjectNotFoundException();
                }

                conference.Seats.Add(seat);
                context.SaveChanges();

                // Don't publish new seats if the conference was never published 
                // (and therefore is not published either).
                if (conference.WasEverPublished) {
                    PublishSeatCreated(conferenceId, seat);
                }
            }
        }

        public ConferenceInfo FindConference(string slug)
        {
            using (var context = new ConferenceContext()) {
                return context.Conferences.FirstOrDefault(x => x.Slug == slug);
            }
        }

        public ConferenceInfo FindConference(string email, string accessCode)
        {
            using (var context = new ConferenceContext()) {
                return context.Conferences.FirstOrDefault(x => x.OwnerEmail == email && x.AccessCode == accessCode);
            }
        }

        public IEnumerable<SeatType> FindSeatTypes(Guid conferenceId)
        {
            using (var context = new ConferenceContext()) {
                return context.Conferences
                        .Include(x => x.Seats)
                        .Where(x => x.Id == conferenceId)
                        .Select(x => x.Seats)
                        .FirstOrDefault() ??
                    Enumerable.Empty<SeatType>();
            }
        }

        public SeatType FindSeatType(Guid seatTypeId)
        {
            using (var context = new ConferenceContext()) {
                return context.Seats.Find(seatTypeId);
            }
        }

        public IEnumerable<Order> FindOrders(Guid conferenceId)
        {
            using (var context = new ConferenceContext()) {
                return context.Orders.Include("Seats.SeatInfo")
                    .Where(x => x.ConferenceId == conferenceId)
                    .ToList();
            }
        }

        public void UpdateConference(ConferenceInfo conference)
        {
            using (var context = new ConferenceContext()) {
                var existing = context.Conferences.Find(conference.Id);
                if (existing == null) {
                    throw new ObjectNotFoundException();
                }

                context.Entry(existing).CurrentValues.SetValues(conference);
                context.SaveChanges();

                PublishConferenceEvent<ConferenceUpdated>(conference);
            }
        }

        public void UpdateSeat(Guid conferenceId, SeatType seat)
        {
            using (var context = new ConferenceContext()) {
                var existing = context.Seats.Find(seat.Id);
                if (existing == null) {
                    throw new ObjectNotFoundException();
                }

                context.Entry(existing).CurrentValues.SetValues(seat);
                context.SaveChanges();

                // Don't publish seat updates if the conference was never published 
                // (and therefore is not published either).
                if (context.Conferences.Where(x => x.Id == conferenceId).Select(x => x.WasEverPublished).FirstOrDefault()) {
                    eventBus.Publish(new SeatUpdated {
                        ConferenceId = conferenceId,
                        SourceId = seat.Id,
                        Name = seat.Name,
                        Description = seat.Description,
                        Price = seat.Price,
                        Quantity = seat.Quantity
                    });
                }
            }
        }

        public void Publish(Guid conferenceId)
        {
            UpdatePublished(conferenceId, true);
        }

        public void Unpublish(Guid conferenceId)
        {
            UpdatePublished(conferenceId, false);
        }

        private void UpdatePublished(Guid conferenceId, bool isPublished)
        {
            using (var context = new ConferenceContext()) {
                var conference = context.Conferences.Find(conferenceId);
                if (conference == null) {
                    throw new ObjectNotFoundException();
                }

                conference.IsPublished = isPublished;
                if (isPublished && !conference.WasEverPublished) {
                    // This flags prevents any further seat type deletions.
                    conference.WasEverPublished = true;
                    context.SaveChanges();

                    // We always publish events *after* saving to store.
                    // Publish all seats that were created before.
                    foreach (var seat in conference.Seats) {
                        PublishSeatCreated(conference.Id, seat);
                    }
                } else {
                    context.SaveChanges();
                }

                if (isPublished) {
                    eventBus.Publish(new ConferencePublished {SourceId = conferenceId});
                } else {
                    eventBus.Publish(new ConferenceUnpublished {SourceId = conferenceId});
                }
            }
        }

        public void DeleteSeat(Guid id)
        {
            using (var context = new ConferenceContext()) {
                var seat = context.Seats.Find(id);
                if (seat == null) {
                    throw new ObjectNotFoundException();
                }

                var wasPublished = context.Conferences
                    .Where(x => x.Seats.Any(s => s.Id == id))
                    .Select(x => x.WasEverPublished)
                    .FirstOrDefault();

                if (wasPublished) {
                    throw new InvalidOperationException("Can't delete seats from a conference that has been published at least once.");
                }

                context.Seats.Remove(seat);
                context.SaveChanges();
            }
        }

        private void PublishConferenceEvent<T>(ConferenceInfo conference)
            where T : ConferenceEvent, new()
        {
            eventBus.Publish(new T {
                SourceId = conference.Id,
                Owner = new Owner {
                    Name = conference.OwnerName,
                    Email = conference.OwnerEmail
                },
                Name = conference.Name,
                Description = conference.Description,
                Location = conference.Location,
                Slug = conference.Slug,
                Tagline = conference.Tagline,
                TwitterSearch = conference.TwitterSearch,
                StartDate = conference.StartDate,
                EndDate = conference.EndDate
            });
        }

        private void PublishSeatCreated(Guid conferenceId, SeatType seat)
        {
            eventBus.Publish(new SeatCreated {
                ConferenceId = conferenceId,
                SourceId = seat.Id,
                Name = seat.Name,
                Description = seat.Description,
                Price = seat.Price,
                Quantity = seat.Quantity
            });
        }
    }
}