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
using Infrastructure.Azure.Messaging;
using Infrastructure.Messaging;
using Infrastructure.Messaging.Handling;
using Moq;
using Xunit;

namespace Infrastructure.Azure.Tests.Messaging.SynchronousCommandBusDecoratorFixture.given_empty_command_bus
{
    public class Context
    {
        protected readonly SynchronousCommandBusDecorator sut;

        protected readonly Mock<ICommandBus> wrappedBusMock;

        public Context()
        {
            wrappedBusMock = new Mock<ICommandBus>();

            sut = new SynchronousCommandBusDecorator(wrappedBusMock.Object);
        }
    }

    public class when_sending_command : Context
    {
        private readonly Envelope<ICommand> command;

        public when_sending_command()
        {
            command = new Envelope<ICommand>(new CommandA());

            sut.Send(command);
        }

        [Fact]
        public void then_forwards_to_wrapped_bus()
        {
            wrappedBusMock.Verify(b => b.Send(command), Times.Once());
        }
    }
}

namespace Infrastructure.Azure.Tests.Messaging.SynchronousCommandBusDecoratorFixture.given_command_bus_with_registered_handlers
{
    public class Context : given_empty_command_bus.Context
    {
        protected readonly List<ICommand> forwarded;

        protected readonly Mock<ICommandHandler<CommandA>> handlerAMock;

        protected readonly Mock<ICommandHandler<CommandB>> handlerBMock;

        protected readonly List<ICommand> synchronous;

        public Context()
        {
            handlerAMock = new Mock<ICommandHandler<CommandA>>();
            handlerBMock = new Mock<ICommandHandler<CommandB>>();

            sut.Register(handlerAMock.Object);
            sut.Register(handlerBMock.Object);

            synchronous = new List<ICommand>();
            forwarded = new List<ICommand>();
            handlerAMock.Setup(h => h.Handle(It.IsAny<CommandA>())).Callback<CommandA>(c => synchronous.Add(c));
            handlerBMock.Setup(h => h.Handle(It.IsAny<CommandB>())).Callback<CommandB>(c => synchronous.Add(c));
            wrappedBusMock
                .Setup(b => b.Send(It.IsAny<IEnumerable<Envelope<ICommand>>>()))
                .Callback<IEnumerable<Envelope<ICommand>>>(es => forwarded.AddRange(es.Select(e => e.Body)));
        }
    }

    public class when_sending_command_for_registered_handler : Context
    {
        private readonly Envelope<ICommand> command;

        public when_sending_command_for_registered_handler()
        {
            command = new Envelope<ICommand>(new CommandA());

            sut.Send(command);
        }

        [Fact]
        public void then_registerd_handler_handles_the_command()
        {
            handlerAMock.Verify(b => b.Handle((CommandA) command.Body), Times.Once());
        }

        [Fact]
        public void then_does_not_forward_to_wrapped_bus()
        {
            wrappedBusMock.Verify(b => b.Send(command), Times.Never());
        }
    }

    public class when_sending_command_for_no_registered_handler : Context
    {
        private readonly Envelope<ICommand> command;

        public when_sending_command_for_no_registered_handler()
        {
            command = new Envelope<ICommand>(new CommandC());

            sut.Send(command);
        }

        [Fact]
        public void then_forwards_to_wrapped_bus()
        {
            wrappedBusMock.Verify(b => b.Send(command), Times.Once());
        }
    }

    public class when_sending_command_for_registered_handler_throws : Context
    {
        private readonly Envelope<ICommand> command;

        public when_sending_command_for_registered_handler_throws()
        {
            handlerAMock.Setup(h => h.Handle(It.IsAny<CommandA>())).Throws<Exception>();
            command = new Envelope<ICommand>(new CommandA());

            sut.Send(command);
        }

        [Fact]
        public void then_registerd_handler_handles_the_command()
        {
            handlerAMock.Verify(b => b.Handle((CommandA) command.Body), Times.Once());
        }

        [Fact]
        public void then_forwards_to_wrapped_bus()
        {
            wrappedBusMock.Verify(b => b.Send(command), Times.Once());
        }
    }

    public class when_sending_multiple_commands_for_registered_handlers : Context
    {
        private readonly List<ICommand> allCommands;

        public when_sending_multiple_commands_for_registered_handlers()
        {
            allCommands =
                new List<ICommand> {
                    new CommandA(),
                    new CommandA(),
                    new CommandB(),
                    new CommandA(),
                    new CommandB(),
                    new CommandA()
                };

            sut.Send(allCommands);
        }

        [Fact]
        public void then_all_commands_are_executed_synchronously()
        {
            Assert.Equal(allCommands, synchronous);
        }

        [Fact]
        public void then_no_commands_are_forwarded_to_bus()
        {
            Assert.Empty(forwarded);
        }
    }

    public class when_sending_multiple_commands_for_registered_and_non_registered_handlers : Context
    {
        private readonly List<ICommand> allCommands;

        public when_sending_multiple_commands_for_registered_and_non_registered_handlers()
        {
            allCommands =
                new List<ICommand> {
                    new CommandA(),
                    new CommandA(),
                    new CommandC(),
                    new CommandA(),
                    new CommandB(),
                    new CommandA()
                };

            sut.Send(allCommands);
        }

        [Fact]
        public void then_all_commands_up_to_the_first_command_with_no_handler_are_executed_synchronously()
        {
            Assert.Equal(allCommands.Take(2), synchronous);
        }

        [Fact]
        public void then_all_commands_starting_with_the_first_command_with_no_handler_are_forwarded_to_the_bus()
        {
            Assert.Equal(allCommands.Skip(2), forwarded);
        }
    }

    public class when_sending_multiple_commands_with_a_handling_failure : Context
    {
        private readonly List<ICommand> allCommands;

        public when_sending_multiple_commands_with_a_handling_failure()
        {
            allCommands =
                new List<ICommand> {
                    new CommandA(),
                    new CommandA(),
                    new CommandB(),
                    new CommandA(),
                    new CommandB(),
                    new CommandA()
                };

            handlerBMock.Setup(h => h.Handle(It.IsAny<CommandB>())).Throws<Exception>();

            sut.Send(allCommands);
        }

        [Fact]
        public void then_all_commands_up_to_the_first_command_with_no_handler_are_executed_synchronously()
        {
            Assert.Equal(allCommands.Take(2), synchronous);
        }

        [Fact]
        public void then_all_commands_starting_with_the_first_command_with_no_handler_are_forwarded_to_the_bus()
        {
            Assert.Equal(allCommands.Skip(2), forwarded);
        }
    }
}

namespace Infrastructure.Azure.Tests.Messaging.SynchronousCommandBusDecoratorFixture
{
    public class CommandA : ICommand
    {
        public Guid Id {
            get { return Guid.Empty; }
        }
    }

    public class CommandB : ICommand
    {
        public Guid Id {
            get { return Guid.Empty; }
        }
    }

    public class CommandC : ICommand
    {
        public Guid Id {
            get { return Guid.Empty; }
        }
    }
}