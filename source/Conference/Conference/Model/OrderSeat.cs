using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conference
{
    public class OrderSeat
    {
        public int Position { get; set; }

        public Guid OrderId { get; set; }

        public Attendee Attendee { get; set; }

        /// <summary>
        ///     Typical pattern for foreign key relationship
        ///     in EF. The identifier is all that's needed
        ///     to persist the referring entity.
        /// </summary>
        [ForeignKey("SeatInfo")]
        public Guid SeatInfoId { get; set; }

        public SeatType SeatInfo { get; set; }

        public OrderSeat(Guid orderId, int position, Guid seatInfoId)
            : this()
        {
            OrderId = orderId;
            Position = position;
            SeatInfoId = seatInfoId;
        }

        protected OrderSeat()
        {
            // Complex type properties can never be 
            // null.
            Attendee = new Attendee();
        }
    }
}