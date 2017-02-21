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
using System.Linq;
using Infrastructure.EventSourcing;
using Registration.Events;

namespace Registration
{
    /// <summary>
    ///     Manages the availability of conference seats. Currently there is one <see cref="SeatsAvailability" /> instance per
    ///     conference.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For more information on the domain, see
    ///         <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258553">Journey chapter 3</see>.
    ///     </para>
    ///     <para>
    ///         Some of the instances of <see cref="SeatsAvailability" /> are highly contentious, as there could be several
    ///         users trying to register for the
    ///         same conference at the same time.
    ///     </para>
    ///     <para>
    ///         Because for large conferences a single instance of <see cref="SeatsAvailability" /> can contain a big event
    ///         stream, the class implements
    ///         <see cref="IMementoOriginator" />, so that a <see cref="IMemento" /> object with the objects' internal state (a
    ///         snapshot) can be cached.
    ///         If the repository supports caching snapshots, then the next time an instance of
    ///         <see cref="SeatsAvailability" /> is created, it can pass
    ///         the cached <see cref="IMemento" /> in the constructor overload, and avoid reading thousands of events from the
    ///         event store.
    ///     </para>
    /// </remarks>
    public class SeatsAvailability : EventSourced, IMementoOriginator
    {
        private readonly Dictionary<Guid, List<SeatQuantity>> pendingReservations = new Dictionary<Guid, List<SeatQuantity>>();

        private readonly Dictionary<Guid, int> remainingSeats = new Dictionary<Guid, int>();

        /// <summary>
        ///     Creates a new instance of the <see cref="SeatsAvailability" /> class.
        /// </summary>
        /// <param name="id">
        ///     The identifier. Currently this correlates to the ConferenceID as specified in
        ///     <see cref="Handlers.SeatsAvailabilityHandler" />.
        /// </param>
        public SeatsAvailability(Guid id)
            : base(id)
        {
            Handles<AvailableSeatsChanged>(OnAvailableSeatsChanged);
            Handles<SeatsReserved>(OnSeatsReserved);
            Handles<SeatsReservationCommitted>(OnSeatsReservationCommitted);
            Handles<SeatsReservationCancelled>(OnSeatsReservationCancelled);
        }

        /// <summary>
        ///     Creates a new instance of the <see cref="SeatsAvailability" /> class, specifying the past events.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <param name="history">The event stream of this event sourced object.</param>
        public SeatsAvailability(Guid id, IEnumerable<IVersionedEvent> history)
            : this(id)
        {
            LoadFrom(history);
        }

        /// <summary>
        ///     Creates a new instance of the <see cref="SeatsAvailability" /> class, specifying a snapshot, and the new events
        ///     since the snapshot was taken.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <param name="memento">A snapshot of the object created by this entity in the past.</param>
        /// <param name="history">The event stream of this event sourced object since the <paramref name="memento" /> was created.</param>
        public SeatsAvailability(Guid id, IMemento memento, IEnumerable<IVersionedEvent> history)
            : this(id)
        {
            var state = (Memento) memento;
            Version = state.Version;
            // make a copy of the state values to avoid concurrency problems with reusing references.
            remainingSeats.AddRange(state.RemainingSeats);
            pendingReservations.AddRange(state.PendingReservations);
            LoadFrom(history);
        }

        public void AddSeats(Guid seatType, int quantity)
        {
            Update(new AvailableSeatsChanged {Seats = new[] {new SeatQuantity(seatType, quantity)}});
        }

        public void RemoveSeats(Guid seatType, int quantity)
        {
            Update(new AvailableSeatsChanged {Seats = new[] {new SeatQuantity(seatType, -quantity)}});
        }

        /// <summary>
        ///     Requests a reservation for seats.
        /// </summary>
        /// <param name="reservationId">A token identifying the reservation request.</param>
        /// <param name="wantedSeats">The list of seat requirements.</param>
        /// <remarks>The reservation id is not the id for an aggregate root, just an opaque identifier.</remarks>
        public void MakeReservation(Guid reservationId, IEnumerable<SeatQuantity> wantedSeats)
        {
            var wantedList = wantedSeats.ToList();
            if (wantedList.Any(x => !remainingSeats.ContainsKey(x.SeatType))) {
                throw new ArgumentOutOfRangeException(nameof(wantedSeats));
            }

            var difference = new Dictionary<Guid, SeatDifference>();

            foreach (var w in wantedList) {
                var item = GetOrAdd(difference, w.SeatType);
                item.Wanted = w.Quantity;
                item.Remaining = remainingSeats[w.SeatType];
            }

            List<SeatQuantity> existing;
            if (pendingReservations.TryGetValue(reservationId, out existing)) {
                foreach (var e in existing) {
                    GetOrAdd(difference, e.SeatType).Existing = e.Quantity;
                }
            }

            var reservation = new SeatsReserved {
                ReservationId = reservationId,
                ReservationDetails = difference.Select(x => new SeatQuantity(x.Key, x.Value.Actual)).Where(x => x.Quantity != 0).ToList(),
                AvailableSeatsChanged = difference.Select(x => new SeatQuantity(x.Key, -x.Value.DeltaSinceLast)).Where(x => x.Quantity != 0).ToList()
            };

            Update(reservation);
        }

        public void CancelReservation(Guid reservationId)
        {
            List<SeatQuantity> reservation;
            if (pendingReservations.TryGetValue(reservationId, out reservation)) {
                Update(new SeatsReservationCancelled {
                    ReservationId = reservationId,
                    AvailableSeatsChanged = reservation.Select(x => new SeatQuantity(x.SeatType, x.Quantity)).ToList()
                });
            }
        }

        public void CommitReservation(Guid reservationId)
        {
            if (pendingReservations.ContainsKey(reservationId)) {
                Update(new SeatsReservationCommitted {ReservationId = reservationId});
            }
        }

        private static TValue GetOrAdd<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key)
            where TValue : new()
        {
            TValue value;
            if (!dictionary.TryGetValue(key, out value)) {
                value = new TValue();
                dictionary[key] = value;
            }

            return value;
        }

        private void OnAvailableSeatsChanged(AvailableSeatsChanged e)
        {
            foreach (var seat in e.Seats) {
                var newValue = seat.Quantity;
                int remaining;
                if (remainingSeats.TryGetValue(seat.SeatType, out remaining)) {
                    newValue += remaining;
                }

                remainingSeats[seat.SeatType] = newValue;
            }
        }

        private void OnSeatsReserved(SeatsReserved e)
        {
            var details = e.ReservationDetails.ToList();
            if (details.Count > 0) {
                pendingReservations[e.ReservationId] = details;
            } else {
                pendingReservations.Remove(e.ReservationId);
            }

            foreach (var seat in e.AvailableSeatsChanged) {
                remainingSeats[seat.SeatType] = remainingSeats[seat.SeatType] + seat.Quantity;
            }
        }

        private void OnSeatsReservationCommitted(SeatsReservationCommitted e)
        {
            pendingReservations.Remove(e.ReservationId);
        }

        private void OnSeatsReservationCancelled(SeatsReservationCancelled e)
        {
            pendingReservations.Remove(e.ReservationId);

            foreach (var seat in e.AvailableSeatsChanged) {
                remainingSeats[seat.SeatType] = remainingSeats[seat.SeatType] + seat.Quantity;
            }
        }

        /// <summary>
        ///     Saves the object's state to an opaque memento object (a snapshot) that can be used to restore the state by using
        ///     the constructor overload.
        /// </summary>
        /// <returns>An opaque memento object that can be used to restore the state.</returns>
        public IMemento SaveToMemento()
        {
            return new Memento {
                Version = Version,
                RemainingSeats = remainingSeats.ToArray(),
                PendingReservations = pendingReservations.ToArray()
            };
        }

        private class SeatDifference
        {
            public int Wanted { get; set; }

            public int Existing { get; set; }

            public int Remaining { get; set; }

            public int Actual {
                get { return Math.Min(Wanted, Math.Max(Remaining, 0) + Existing); }
            }

            public int DeltaSinceLast {
                get { return Actual - Existing; }
            }
        }

        internal class Memento : IMemento
        {
            internal KeyValuePair<Guid, int>[] RemainingSeats { get; set; }

            internal KeyValuePair<Guid, List<SeatQuantity>>[] PendingReservations { get; set; }

            public int Version { get; internal set; }
        }
    }
}