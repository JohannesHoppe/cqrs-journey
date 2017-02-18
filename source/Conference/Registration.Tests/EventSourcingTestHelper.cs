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
using Infrastructure;
using Infrastructure.EventSourcing;
using Infrastructure.Messaging;
using Infrastructure.Messaging.Handling;
using Xunit;

namespace Registration.Tests
{
    public class EventSourcingTestHelper<T>
        where T : IEventSourced
    {
        private readonly RepositoryStub repository;

        private string expectedCorrelationid;

        private ICommandHandler handler;

        public List<IVersionedEvent> Events { get; }

        public IEventSourcedRepository<T> Repository {
            get { return repository; }
        }

        public EventSourcingTestHelper()
        {
            Events = new List<IVersionedEvent>();
            repository =
                new RepositoryStub((eventSouced, correlationId) => {
                    if (expectedCorrelationid != null) {
                        Assert.Equal(expectedCorrelationid, correlationId);
                    }

                    Events.AddRange(eventSouced.Events);
                });
        }

        public void Setup(ICommandHandler handler)
        {
            this.handler = handler;
        }

        public void Given(params IVersionedEvent[] history)
        {
            repository.History.AddRange(history);
        }

        public void When(ICommand command)
        {
            expectedCorrelationid = command.Id.ToString();
            ((dynamic) handler).Handle((dynamic) command);
            expectedCorrelationid = null;
        }

        public void When(IEvent @event)
        {
            ((dynamic) handler).Handle((dynamic) @event);
        }

        public bool ThenContains<TEvent>()
            where TEvent : IVersionedEvent
        {
            return Events.Any(x => x.GetType() == typeof(TEvent));
        }

        public TEvent ThenHasSingle<TEvent>()
            where TEvent : IVersionedEvent
        {
            Assert.Equal(1, Events.Count);
            var @event = Events.Single();
            Assert.IsAssignableFrom<TEvent>(@event);
            return (TEvent) @event;
        }

        public TEvent ThenHasOne<TEvent>()
            where TEvent : IVersionedEvent
        {
            Assert.Equal(1, Events.OfType<TEvent>().Count());
            var @event = Events.OfType<TEvent>().Single();
            return @event;
        }

        private class RepositoryStub : IEventSourcedRepository<T>
        {
            private readonly Func<Guid, IEnumerable<IVersionedEvent>, T> entityFactory;

            public readonly List<IVersionedEvent> History = new List<IVersionedEvent>();

            private readonly Action<T, string> onSave;

            internal RepositoryStub(Action<T, string> onSave)
            {
                this.onSave = onSave;
                var constructor = typeof(T).GetConstructor(new[] {typeof(Guid), typeof(IEnumerable<IVersionedEvent>)});
                if (constructor == null) {
                    throw new InvalidCastException(
                        "Type T must have a constructor with the following signature: .ctor(Guid, IEnumerable<IVersionedEvent>)");
                }
                entityFactory = (id, events) => (T) constructor.Invoke(new object[] {id, events});
            }

            T IEventSourcedRepository<T>.Find(Guid id)
            {
                var all = History.Where(x => x.SourceId == id).ToList();
                if (all.Count > 0) {
                    return entityFactory.Invoke(id, all);
                }

                return default(T);
            }

            void IEventSourcedRepository<T>.Save(T eventSourced, string correlationId)
            {
                onSave(eventSourced, correlationId);
            }

            T IEventSourcedRepository<T>.Get(Guid id)
            {
                var entity = ((IEventSourcedRepository<T>) this).Find(id);
                if (Equals(entity, default(T))) {
                    throw new EntityNotFoundException(id, "Test");
                }

                return entity;
            }
        }
    }
}