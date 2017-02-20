using System;
using Infrastructure.Messaging;

namespace Conference
{
    /// <summary>
    ///     Event published whenever a previously public conference
    ///     is made private by unpublishing it.
    /// </summary>
    public class ConferenceUnpublished : IEvent
    {
        public Guid SourceId { get; set; }
    }
}