﻿// ==============================================================================================================
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
using System.Data.Entity.Infrastructure;
using System.Threading;
using Infrastructure.Sql.Messaging;
using Infrastructure.Sql.Messaging.Implementation;
using Xunit;

namespace Infrastructure.Sql.IntegrationTests.Messaging.MessageReceiverFixture
{
    public class given_sender_and_receiver : IDisposable
    {
        private readonly IDbConnectionFactory connectionFactory;

        private readonly TestableMessageReceiver receiver;

        private readonly MessageSender sender;

        public given_sender_and_receiver()
        {
            connectionFactory = System.Data.Entity.Database.DefaultConnectionFactory;
            sender = new MessageSender(connectionFactory, "TestSqlMessaging", "Test.Commands");
            receiver = new TestableMessageReceiver(connectionFactory);

            MessagingDbInitializer.CreateDatabaseObjects(connectionFactory.CreateConnection("TestSqlMessaging").ConnectionString, "Test", true);
        }

        [Fact]
        public void when_sending_message_then_receives_message()
        {
            Message message = null;

            receiver.MessageReceived += (s, e) => { message = e.Message; };

            sender.Send(new Message("test message"));

            Assert.True(receiver.ReceiveMessage());
            Assert.Equal("test message", message.Body);
            Assert.Null(message.CorrelationId);
            Assert.Null(message.DeliveryDate);
        }

        [Fact]
        public void when_sending_message_with_correlation_id_then_receives_message()
        {
            Message message = null;

            receiver.MessageReceived += (s, e) => { message = e.Message; };

            sender.Send(new Message("test message", correlationId: "correlation"));

            Assert.True(receiver.ReceiveMessage());
            Assert.Equal("test message", message.Body);
            Assert.Equal("correlation", message.CorrelationId);
            Assert.Null(message.DeliveryDate);
        }

        [Fact]
        public void when_successfully_handles_message_then_removes_message()
        {
            receiver.MessageReceived += (s, e) => { };

            sender.Send(new Message("test message"));

            Assert.True(receiver.ReceiveMessage());
            Assert.False(receiver.ReceiveMessage());
        }

        [Fact]
        public void when_unsuccessfully_handles_message_then_does_not_remove_message()
        {
            EventHandler<MessageReceivedEventArgs> failureHandler = null;
            failureHandler = (s, e) => {
                receiver.MessageReceived -= failureHandler;
                throw new ArgumentException();
            };

            receiver.MessageReceived += failureHandler;

            sender.Send(new Message("test message"));

            try {
                Assert.True(receiver.ReceiveMessage());
                Assert.False(true, "should have thrown");
            } catch (ArgumentException) { }

            Assert.True(receiver.ReceiveMessage());
        }

        [Fact]
        public void when_sending_message_with_delay_then_receives_message_after_delay()
        {
            Message message = null;

            receiver.MessageReceived += (s, e) => { message = e.Message; };

            var deliveryDate = DateTime.UtcNow.Add(TimeSpan.FromSeconds(5));
            sender.Send(new Message("test message", deliveryDate));

            Assert.False(receiver.ReceiveMessage());

            Thread.Sleep(TimeSpan.FromSeconds(6));

            Assert.True(receiver.ReceiveMessage());
            Assert.Equal("test message", message.Body);
        }

        [Fact]
        public void when_receiving_message_then_other_receivers_cannot_see_message_but_see_other_messages()
        {
            var secondReceiver = new TestableMessageReceiver(connectionFactory);

            sender.Send(new Message("message1"));
            sender.Send(new Message("message2"));

            var waitEvent = new AutoResetEvent(false);
            string receiver1Message = null;
            string receiver2Message = null;

            receiver.MessageReceived += (s, e) => {
                waitEvent.Set();
                receiver1Message = e.Message.Body;
                waitEvent.WaitOne();
            };
            secondReceiver.MessageReceived += (s, e) => { receiver2Message = e.Message.Body; };

            ThreadPool.QueueUserWorkItem(_ => { receiver.ReceiveMessage(); });

            Assert.True(waitEvent.WaitOne(TimeSpan.FromSeconds(10)));
            secondReceiver.ReceiveMessage();
            waitEvent.Set();

            Assert.Equal("message1", receiver1Message);
            Assert.Equal("message2", receiver2Message);
        }

        [Fact]
        public void when_receiving_message_then_can_send_new_message()
        {
            var secondReceiver = new TestableMessageReceiver(connectionFactory);

            sender.Send(new Message("message1"));

            var waitEvent = new AutoResetEvent(false);
            string receiver1Message = null;
            string receiver2Message = null;

            receiver.MessageReceived += (s, e) => {
                waitEvent.Set();
                receiver1Message = e.Message.Body;
                waitEvent.WaitOne();
            };
            secondReceiver.MessageReceived += (s, e) => { receiver2Message = e.Message.Body; };

            ThreadPool.QueueUserWorkItem(_ => { receiver.ReceiveMessage(); });

            Assert.True(waitEvent.WaitOne(TimeSpan.FromSeconds(10)));
            sender.Send(new Message("message2"));
            secondReceiver.ReceiveMessage();
            waitEvent.Set();

            Assert.Equal("message1", receiver1Message);
            Assert.Equal("message2", receiver2Message);
        }

        void IDisposable.Dispose()
        {
            receiver.Stop();

            using (var connection = connectionFactory.CreateConnection("TestSqlMessaging")) {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "TRUNCATE TABLE Test.Commands";
                command.ExecuteNonQuery();
            }
        }

        public class TestableMessageReceiver : MessageReceiver
        {
            public TestableMessageReceiver(IDbConnectionFactory connectionFactory)
                : base(connectionFactory, "TestSqlMessaging", "Test.Commands") { }

            public new bool ReceiveMessage()
            {
                return base.ReceiveMessage();
            }
        }
    }
}