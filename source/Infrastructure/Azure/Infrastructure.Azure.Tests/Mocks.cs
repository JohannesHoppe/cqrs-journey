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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.Azure.Messaging;
using Infrastructure.EventSourcing;
using Microsoft.ServiceBus.Messaging;

namespace Infrastructure.Azure.Tests.Mocks
{
    internal class TestEventComparer : IEqualityComparer<IVersionedEvent>
    {
        public bool Equals(IVersionedEvent x, IVersionedEvent y)
        {
            return x.SourceId == y.SourceId && x.Version == y.Version && ((TestEvent) x).Foo == ((TestEvent) y).Foo;
        }

        public int GetHashCode(IVersionedEvent obj)
        {
            throw new NotImplementedException();
        }
    }

    internal class TestEntity : IEventSourced
    {
        public IEnumerable<IVersionedEvent> History { get; set; }

        public List<IVersionedEvent> Events { get; set; }

        public TestEntity()
        {
            Events = new List<IVersionedEvent>();
        }

        public TestEntity(Guid id, IEnumerable<IVersionedEvent> history)
        {
            Events = new List<IVersionedEvent>();
            History = history;
            Id = id;
        }

        public Guid Id { get; set; }

        public int Version { get; set; }

        IEnumerable<IVersionedEvent> IEventSourced.Events {
            get { return Events; }
        }
    }

    internal class TestOriginatorEntity : IEventSourced, IMementoOriginator
    {
        public IEnumerable<IVersionedEvent> History { get; set; }

        public IMemento Memento { get; set; }

        public List<IVersionedEvent> Events { get; set; }

        public TestOriginatorEntity()
        {
            Events = new List<IVersionedEvent>();
        }

        public TestOriginatorEntity(Guid id, IEnumerable<IVersionedEvent> history)
        {
            Events = new List<IVersionedEvent>();
            History = history;
            Id = id;
        }

        public TestOriginatorEntity(Guid id, IMemento memento, IEnumerable<IVersionedEvent> history)
        {
            Events = new List<IVersionedEvent>();
            Memento = memento;
            History = history;
            Id = id;
        }

        public Guid Id { get; set; }

        public int Version { get; set; }

        IEnumerable<IVersionedEvent> IEventSourced.Events {
            get { return Events; }
        }

        IMemento IMementoOriginator.SaveToMemento()
        {
            return Memento;
        }
    }

    internal class TestEvent : IVersionedEvent
    {
        public string Foo { get; set; }

        public Guid SourceId { get; set; }

        public int Version { get; set; }
    }

    internal class MessageSenderMock : IMessageSender
    {
        public readonly ConcurrentBag<Action> AsyncSuccessCallbacks = new ConcurrentBag<Action>();

        public readonly AutoResetEvent SendSignal = new AutoResetEvent(false);

        public readonly ConcurrentBag<BrokeredMessage> Sent = new ConcurrentBag<BrokeredMessage>();

        public bool ShouldWaitForCallback { get; set; }

        void IMessageSender.Send(Func<BrokeredMessage> messageFactory)
        {
            Sent.Add(messageFactory.Invoke());
            SendSignal.Set();
        }

        void IMessageSender.SendAsync(Func<BrokeredMessage> messageFactory)
        {
            throw new NotImplementedException();
        }

        void IMessageSender.SendAsync(Func<BrokeredMessage> messageFactory, Action successCallback, Action<Exception> exceptionCallback)
        {
            Task.Factory.StartNew(
                () => {
                    Sent.Add(messageFactory.Invoke());
                    SendSignal.Set();
                    if (!ShouldWaitForCallback) {
                        successCallback();
                    } else {
                        AsyncSuccessCallbacks.Add(successCallback);
                    }
                },
                TaskCreationOptions.AttachedToParent);
        }

        public event EventHandler Retrying;
    }
}