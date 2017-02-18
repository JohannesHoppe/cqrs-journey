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
using Infrastructure.Messaging;
using Infrastructure.Messaging.Handling;
using Moq;
using Xunit;

namespace Infrastructure.Tests.Messaging.Handling.MessageDispatcherFixture
{
    public class given_empty_dispatcher
    {
        private readonly EventDispatcher sut;

        public given_empty_dispatcher()
        {
            sut = new EventDispatcher();
        }

        [Fact]
        public void when_dispatching_an_event_then_does_nothing()
        {
            var @event = new EventC();

            sut.DispatchMessage(@event, "message", "correlation", "");
        }
    }

    public class given_dispatcher_with_handler
    {
        private readonly Mock<IEventHandler> handlerMock;

        private readonly EventDispatcher sut;

        public given_dispatcher_with_handler()
        {
            sut = new EventDispatcher();

            handlerMock = new Mock<IEventHandler>();
            handlerMock.As<IEventHandler<EventA>>();

            sut.Register(handlerMock.Object);
        }

        [Fact]
        public void when_dispatching_an_event_with_registered_handler_then_invokes_handler()
        {
            var @event = new EventA();

            sut.DispatchMessage(@event, "message", "correlation", "");

            handlerMock.As<IEventHandler<EventA>>().Verify(h => h.Handle(@event), Times.Once());
        }

        [Fact]
        public void when_dispatching_an_event_with_no_registered_handler_then_does_nothing()
        {
            var @event = new EventC();

            sut.DispatchMessage(@event, "message", "correlation", "");
        }
    }

    public class given_dispatcher_with_handler_for_envelope
    {
        private readonly Mock<IEventHandler> handlerMock;

        private readonly EventDispatcher sut;

        public given_dispatcher_with_handler_for_envelope()
        {
            sut = new EventDispatcher();

            handlerMock = new Mock<IEventHandler>();
            handlerMock.As<IEnvelopedEventHandler<EventA>>();

            sut.Register(handlerMock.Object);
        }

        [Fact]
        public void when_dispatching_an_event_with_registered_handler_then_invokes_handler()
        {
            var @event = new EventA();

            sut.DispatchMessage(@event, "message", "correlation", "");

            handlerMock.As<IEnvelopedEventHandler<EventA>>()
                .Verify(
                    h => h.Handle(It.Is<Envelope<EventA>>(e => e.Body == @event && e.MessageId == "message" && e.CorrelationId == "correlation")),
                    Times.Once());
        }

        [Fact]
        public void when_dispatching_an_event_with_no_registered_handler_then_does_nothing()
        {
            var @event = new EventC();

            sut.DispatchMessage(@event, "message", "correlation", "");
        }
    }

    public class given_dispatcher_with_multiple_handlers
    {
        private readonly Mock<IEventHandler> handler1Mock;

        private readonly Mock<IEventHandler> handler2Mock;

        private readonly EventDispatcher sut;

        public given_dispatcher_with_multiple_handlers()
        {
            sut = new EventDispatcher();

            handler1Mock = new Mock<IEventHandler>();
            handler1Mock.As<IEnvelopedEventHandler<EventA>>();
            handler1Mock.As<IEventHandler<EventB>>();

            sut.Register(handler1Mock.Object);

            handler2Mock = new Mock<IEventHandler>();
            handler2Mock.As<IEventHandler<EventA>>();

            sut.Register(handler2Mock.Object);
        }

        [Fact]
        public void when_dispatching_an_event_with_multiple_registered_handlers_then_invokes_handlers()
        {
            var @event = new EventA();

            sut.DispatchMessage(@event, "message", "correlation", "");

            handler1Mock.As<IEnvelopedEventHandler<EventA>>()
                .Verify(
                    h => h.Handle(It.Is<Envelope<EventA>>(e => e.Body == @event && e.MessageId == "message" && e.CorrelationId == "correlation")),
                    Times.Once());
            handler2Mock.As<IEventHandler<EventA>>().Verify(h => h.Handle(@event), Times.Once());
        }

        [Fact]
        public void when_dispatching_an_event_with_single_registered_handler_then_invokes_handler()
        {
            var @event = new EventB();

            sut.DispatchMessage(@event, "message", "correlation", "");

            handler1Mock.As<IEventHandler<EventB>>().Verify(h => h.Handle(@event), Times.Once());
        }

        [Fact]
        public void when_dispatching_an_event_with_no_registered_handler_then_does_nothing()
        {
            var @event = new EventC();

            sut.DispatchMessage(@event, "message", "correlation", "");
        }
    }

    public class EventA : IEvent
    {
        public Guid SourceId {
            get { return Guid.Empty; }
        }
    }

    public class EventB : IEvent
    {
        public Guid SourceId {
            get { return Guid.Empty; }
        }
    }

    public class EventC : IEvent
    {
        public Guid SourceId {
            get { return Guid.Empty; }
        }
    }
}