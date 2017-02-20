using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.Linq;
using Infrastructure.Messaging;
using Moq;
using Xunit;

namespace Conference.IntegrationTests.ConferenceServiceFixture
{
    public class given_no_conference : IDisposable
    {
        private readonly List<IEvent> busEvents;

        private readonly ConferenceService service;

        public given_no_conference()
        {
            using (var context = new ConferenceContext()) {
                if (context.Database.Exists()) {
                    context.Database.Delete();
                }

                context.Database.Create();
            }

            busEvents = new List<IEvent>();
            var busMock = new Mock<IEventBus>();
            busMock.Setup(b => b.Publish(It.IsAny<Envelope<IEvent>>())).Callback<Envelope<IEvent>>(e => busEvents.Add(e.Body));
            busMock.Setup(b => b.Publish(It.IsAny<IEnumerable<Envelope<IEvent>>>())).Callback<IEnumerable<Envelope<IEvent>>>(es => busEvents.AddRange(es.Select(e => e.Body)));

            service = new ConferenceService(busMock.Object);
        }

        [Fact]
        public void when_finding_by_non_existing_slug_then_returns_null()
        {
            var conference = service.FindConference(Guid.NewGuid().ToString());

            Assert.Null(conference);
        }

        [Fact]
        public void when_finding_by_non_existing_email_and_access_code_then_returns_null()
        {
            var conference = service.FindConference("foo@bar.com", Guid.NewGuid().ToString());

            Assert.Null(conference);
        }

        [Fact]
        public void when_finding_seats_by_non_existing_conference_id_then_returns_empty()
        {
            var conference = service.FindSeatTypes(Guid.NewGuid());

            Assert.Empty(conference);
        }

        [Fact]
        public void when_creating_seat_then_throws()
        {
            Assert.Throws<ObjectNotFoundException>(() => service.CreateSeat(Guid.NewGuid(), new SeatType()));
        }

        [Fact]
        public void when_updating_non_existing_conference_then_throws()
        {
            Assert.Throws<ObjectNotFoundException>(() => service.UpdateConference(new ConferenceInfo()));
        }

        [Fact]
        public void when_updating_seat_for_non_existing_conference_then_throws()
        {
            Assert.Throws<ObjectNotFoundException>(() => service.UpdateSeat(Guid.NewGuid(), new SeatType()));
        }

        [Fact]
        public void when_updating_published_non_existing_conference_then_throws()
        {
            Assert.Throws<ObjectNotFoundException>(() => service.Unpublish(Guid.NewGuid()));
        }

        [Fact]
        public void when_deleting_non_existing_seat_then_throws()
        {
            Assert.Throws<ObjectNotFoundException>(() => service.DeleteSeat(Guid.NewGuid()));
        }

        [Fact]
        public void when_creating_conference_and_seat_then_does_not_publish_seat_created()
        {
            var conference = new ConferenceInfo {
                OwnerEmail = "test@contoso.com",
                OwnerName = "test owner",
                AccessCode = "qwerty",
                Name = "test conference",
                Description = "test conference description",
                Location = "redmond",
                Slug = "test",
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.Add(TimeSpan.FromDays(2))
            };
            service.CreateConference(conference);

            var seat = new SeatType {Name = "seat", Description = "description", Price = 100, Quantity = 100};
            service.CreateSeat(conference.Id, seat);

            Assert.Empty(busEvents.OfType<SeatCreated>());
        }

        [Fact]
        public void when_creating_conference_and_seat_then_publishes_seat_created_on_publish()
        {
            var conference = new ConferenceInfo {
                OwnerEmail = "test@contoso.com",
                OwnerName = "test owner",
                AccessCode = "qwerty",
                Name = "test conference",
                Description = "test conference description",
                Location = "redmond",
                Slug = "test",
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.Add(TimeSpan.FromDays(2))
            };
            service.CreateConference(conference);

            var seat = new SeatType {Name = "seat", Description = "description", Price = 100, Quantity = 100};
            service.CreateSeat(conference.Id, seat);

            service.Publish(conference.Id);

            var e = busEvents.OfType<SeatCreated>().FirstOrDefault();

            Assert.NotNull(e);
            Assert.Equal(e.SourceId, seat.Id);
        }

        public void Dispose()
        {
            using (var context = new ConferenceContext()) {
                if (context.Database.Exists()) {
                    context.Database.Delete();
                }
            }
        }
    }

    public class given_an_existing_conference_with_a_seat : IDisposable
    {
        private readonly ConferenceService service;

        protected List<IEvent> busEvents;

        protected ConferenceInfo conference;

        protected string dbName = "ConferenceServiceTests_" + Guid.NewGuid();

        public given_an_existing_conference_with_a_seat()
        {
            using (var context = new ConferenceContext()) {
                if (context.Database.Exists()) {
                    context.Database.Delete();
                }

                context.Database.CreateIfNotExists();
            }

            busEvents = new List<IEvent>();
            var busMock = new Mock<IEventBus>();
            busMock.Setup(b => b.Publish(It.IsAny<Envelope<IEvent>>())).Callback<Envelope<IEvent>>(e => busEvents.Add(e.Body));
            busMock.Setup(b => b.Publish(It.IsAny<IEnumerable<Envelope<IEvent>>>())).Callback<IEnumerable<Envelope<IEvent>>>(es => busEvents.AddRange(es.Select(e => e.Body)));
            service = new ConferenceService(busMock.Object);
            conference = new ConferenceInfo {
                OwnerEmail = "test@contoso.com",
                OwnerName = "test owner",
                AccessCode = "qwerty",
                Name = "test conference",
                Description = "test conference description",
                Location = "redmond",
                Slug = "test",
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.Add(TimeSpan.FromDays(2)),
                IsPublished = true,
                Seats = {
                    new SeatType {
                        Name = "general",
                        Description = "general description",
                        Price = 100,
                        Quantity = 10
                    }
                }
            };
            service.CreateConference(conference);
        }

        [Fact]
        public void then_conference_is_created_unpublished()
        {
            using (var context = new ConferenceContext()) {
                Assert.False(context.Conferences.Find(conference.Id).IsPublished);
                Assert.False(context.Conferences.Find(conference.Id).WasEverPublished);
            }
        }

        [Fact]
        public void then_conference_is_persisted()
        {
            using (var context = new ConferenceContext()) {
                Assert.NotNull(context.Conferences.Find(conference.Id));
            }
        }

        [Fact]
        public void then_conference_created_is_published()
        {
            var e = busEvents.OfType<ConferenceCreated>().Single();

            Assert.NotNull(e);
            Assert.Equal(conference.Id, e.SourceId);
        }

        [Fact]
        public void then_seat_created_is_published_on_publish()
        {
            service.Publish(conference.Id);

            var e = busEvents.OfType<SeatCreated>().Single();
            var seat = conference.Seats.Single();

            Assert.Equal(seat.Id, e.SourceId);
            Assert.Equal(seat.Name, e.Name);
            Assert.Equal(seat.Description, e.Description);
            Assert.Equal(seat.Price, e.Price);
            Assert.Equal(seat.Quantity, e.Quantity);
        }

        [Fact]
        public void when_finding_by_slug_then_returns_conference()
        {
            var conference = service.FindConference(this.conference.Slug);

            Assert.NotNull(conference);
        }

        [Fact]
        public void when_finding_by_existing_email_and_access_code_then_returns_conference()
        {
            var conference = service.FindConference(this.conference.OwnerEmail, this.conference.AccessCode);

            Assert.NotNull(conference);
        }

        [Fact]
        public void when_finding_seats_by_non_existing_conference_id_then_returns_empty()
        {
            var conference = service.FindSeatTypes(this.conference.Id);

            Assert.NotEmpty(conference);
        }

        [Fact]
        public void when_creating_conference_with_existing_slug_then_throws()
        {
            conference.Id = Guid.NewGuid();

            Assert.Throws<DuplicateNameException>(() => service.CreateConference(conference));
        }

        [Fact]
        public void when_creating_seat_then_adds_to_conference()
        {
            var seat = new SeatType {
                Name = "precon",
                Description = "precon desc",
                Price = 100,
                Quantity = 100
            };

            service.CreateSeat(conference.Id, seat);

            var seats = service.FindSeatTypes(conference.Id);

            Assert.Equal(2, seats.Count());
        }

        [Fact]
        public void when_creating_seat_then_seat_created_is_published()
        {
            service.Publish(conference.Id);

            var seat = new SeatType {
                Name = "precon",
                Description = "precon desc",
                Price = 100,
                Quantity = 100
            };

            service.CreateSeat(conference.Id, seat);

            var e = busEvents.OfType<SeatCreated>().Single(x => x.SourceId == seat.Id);

            Assert.Equal(conference.Id, e.ConferenceId);
            Assert.Equal(seat.Id, e.SourceId);
            Assert.Equal(seat.Name, e.Name);
            Assert.Equal(seat.Description, e.Description);
            Assert.Equal(seat.Price, e.Price);
            Assert.Equal(seat.Quantity, e.Quantity);
        }

        [Fact]
        public void when_creating_seat_then_can_find_seat()
        {
            var seat = new SeatType {
                Name = "precon",
                Description = "precon desc",
                Price = 100,
                Quantity = 100
            };

            service.CreateSeat(conference.Id, seat);

            Assert.NotNull(service.FindSeatType(seat.Id));
        }

        [Fact]
        public void when_updating_conference_then_can_find_updated_information()
        {
            conference.Name = "foo";
            conference.Description = "bar";
            conference.Seats.Clear();

            service.UpdateConference(conference);

            var saved = service.FindConference(conference.Slug);

            Assert.Equal(conference.Name, saved.Name);
            Assert.Equal(conference.Description, saved.Description);
        }

        [Fact]
        public void when_updating_non_existing_seat_then_throws()
        {
            Assert.Throws<ObjectNotFoundException>(() => service.UpdateSeat(conference.Id, new SeatType()));
        }

        [Fact]
        public void when_updating_seat_then_can_find_updated_information()
        {
            var seat = conference.Seats.First();
            seat.Name = "precon";
            seat.Description = "precon desc";
            seat.Price = 200;

            service.UpdateSeat(conference.Id, seat);

            var saved = service.FindSeatType(seat.Id);

            Assert.Equal(seat.Name, saved.Name);
            Assert.Equal(seat.Description, saved.Description);
            Assert.Equal(seat.Quantity, saved.Quantity);
        }

        [Fact]
        public void when_updating_seat_then_seat_updated_event_is_published()
        {
            service.Publish(conference.Id);

            var seat = conference.Seats.First();
            seat.Name = "precon";
            seat.Description = "precon desc";
            seat.Price = 200;
            seat.Quantity = 1000;

            service.UpdateSeat(conference.Id, seat);

            var e = busEvents.OfType<SeatUpdated>().LastOrDefault();

            Assert.Equal(conference.Id, e.ConferenceId);
            Assert.Equal(seat.Id, e.SourceId);
            Assert.Equal("precon", e.Name);
            Assert.Equal("precon desc", e.Description);
            Assert.Equal(200, e.Price);
            Assert.Equal(1000, e.Quantity);
        }

        [Fact]
        public void when_updating_published_then_updates_conference()
        {
            service.Publish(conference.Id);

            Assert.True(service.FindConference(conference.Slug).IsPublished);

            service.Unpublish(conference.Id);

            Assert.False(service.FindConference(conference.Slug).IsPublished);
        }

        [Fact]
        public void when_updating_published_then_sets_conference_ever_published()
        {
            service.Publish(conference.Id);

            Assert.True(service.FindConference(conference.Slug).WasEverPublished);
        }

        [Fact]
        public void when_updating_published_to_false_then_conference_ever_published_remains_true()
        {
            service.Publish(conference.Id);
            service.Unpublish(conference.Id);

            Assert.True(service.FindConference(conference.Slug).WasEverPublished);
        }

        [Fact]
        public void when_deleting_seat_then_updates_conference_seats()
        {
            Assert.Equal(1, service.FindSeatTypes(conference.Id).Count());

            service.DeleteSeat(conference.Seats.First().Id);

            Assert.Equal(0, service.FindSeatTypes(conference.Id).Count());
        }

        [Fact]
        public void when_deleting_seat_from_published_conference_then_throws()
        {
            service.Publish(conference.Id);

            Assert.Throws<InvalidOperationException>(() => service.DeleteSeat(conference.Seats.First().Id));
        }

        [Fact]
        public void when_deleting_seat_from_previously_published_conference_then_throws()
        {
            service.Publish(conference.Id);
            service.Unpublish(conference.Id);

            Assert.Throws<InvalidOperationException>(() => service.DeleteSeat(conference.Seats.First().Id));
        }

        public void Dispose()
        {
            using (var context = new ConferenceContext()) {
                if (context.Database.Exists()) {
                    context.Database.Delete();
                }
            }
        }
    }
}