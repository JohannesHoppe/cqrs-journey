using System;
using Infrastructure.Messaging;

namespace Conference
{
    /// <summary>
    ///     Event raised when a new seat type is created. Note
    ///     that when a seat type is created.
    /// </summary>
    public class SeatCreated : IEvent
    {
        /// <summary>
        ///     Gets or sets the conference identifier.
        /// </summary>
        public Guid ConferenceId { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public decimal Price { get; set; }

        public int Quantity { get; set; }

        /// <summary>
        ///     Gets or sets the source seat type identifier.
        /// </summary>
        public Guid SourceId { get; set; }
    }
}