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
using Infrastructure.Messaging;

namespace Infrastructure.EventSourcing
{
    /// <summary>
    ///     Base class for event sourced entities that implements <see cref="IEventSourced" />.
    /// </summary>
    /// <remarks>
    ///     <see cref="IEventSourced" /> entities do not require the use of <see cref="EventSourced" />, but this class
    ///     contains some common
    ///     useful functionality related to versions and rehydration from past events.
    /// </remarks>
    public abstract class EventSourced : IEventSourced
    {
        private readonly Dictionary<Type, Action<IVersionedEvent>> handlers = new Dictionary<Type, Action<IVersionedEvent>>();

        private readonly List<IVersionedEvent> pendingEvents = new List<IVersionedEvent>();

        protected EventSourced(Guid id)
        {
            Id = id;
        }

        /// <summary>
        ///     Configures a handler for an event.
        /// </summary>
        protected void Handles<TEvent>(Action<TEvent> handler)
            where TEvent : IEvent
        {
            handlers.Add(typeof(TEvent), @event => handler((TEvent) @event));
        }

        protected void LoadFrom(IEnumerable<IVersionedEvent> pastEvents)
        {
            foreach (var e in pastEvents) {
                handlers[e.GetType()].Invoke(e);
                Version = e.Version;
            }
        }

        protected void Update(VersionedEvent e)
        {
            e.SourceId = Id;
            e.Version = Version + 1;
            handlers[e.GetType()].Invoke(e);
            Version = e.Version;
            pendingEvents.Add(e);
        }

        public Guid Id { get; }

        /// <summary>
        ///     Gets the entity's version. As the entity is being updated and events being generated, the version is incremented.
        /// </summary>
        public int Version { get; protected set; } = -1;

        /// <summary>
        ///     Gets the collection of new events since the entity was loaded, as a consequence of command handling.
        /// </summary>
        public IEnumerable<IVersionedEvent> Events {
            get { return pendingEvents; }
        }
    }
}