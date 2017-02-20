using System;
using Infrastructure.Messaging;

namespace Conference
{
    /// <summary>
    ///     Base class for conference-related events, containing
    ///     all the conference information.
    /// </summary>
    public abstract class ConferenceEvent : IEvent
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public string Location { get; set; }

        public string Slug { get; set; }

        public string Tagline { get; set; }

        public string TwitterSearch { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public Owner Owner { get; set; }

        public Guid SourceId { get; set; }
    }
}