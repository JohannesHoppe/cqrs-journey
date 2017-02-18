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
using System.Threading;
using Infrastructure.Azure.Messaging;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Infrastructure.Azure.IntegrationTests.TopicSenderIntegration
{
    public class given_a_topic_sender : given_a_topic_and_subscription
    {
        private readonly SubscriptionClient subscriptionClient;

        private readonly TestableTopicSender sut;

        public given_a_topic_sender()
        {
            sut = new TestableTopicSender(Settings, Topic, new Incremental(1, TimeSpan.Zero, TimeSpan.Zero));

            var tokenProvider = TokenProvider.CreateSharedSecretTokenProvider(Settings.TokenIssuer, Settings.TokenAccessKey);
            var serviceUri = ServiceBusEnvironment.CreateServiceUri(Settings.ServiceUriScheme, Settings.ServiceNamespace, Settings.ServicePath);

            var manager = new NamespaceManager(serviceUri, tokenProvider);
            manager.CreateSubscription(Topic, "Test");

            var messagingFactory = MessagingFactory.Create(serviceUri, tokenProvider);
            subscriptionClient = messagingFactory.CreateSubscriptionClient(Topic, "Test");
        }

        [Fact]
        public void when_sending_message_async_then_succeeds()
        {
            var payload = Guid.NewGuid().ToString();

            sut.SendAsync(() => new BrokeredMessage(payload));

            var message = subscriptionClient.Receive(TimeSpan.FromSeconds(5));
            Assert.Equal(payload, message.GetBody<string>());
        }

        [Fact]
        public void when_sending_message_batch_async_then_succeeds()
        {
            var payload1 = Guid.NewGuid().ToString();
            var payload2 = Guid.NewGuid().ToString();

            sut.SendAsync(new Func<BrokeredMessage>[] {() => new BrokeredMessage(payload1), () => new BrokeredMessage(payload2)});

            var messages = new List<string> {
                subscriptionClient.Receive(TimeSpan.FromSeconds(5)).GetBody<string>(),
                subscriptionClient.Receive(TimeSpan.FromSeconds(2)).GetBody<string>()
            };
            Assert.Contains(payload1, messages);
            Assert.Contains(payload2, messages);
        }

        [Fact]
        public void when_sending_message_then_succeeds()
        {
            var payload = Guid.NewGuid().ToString();
            sut.Send(() => new BrokeredMessage(payload));

            var message = subscriptionClient.Receive();
            Assert.Equal(payload, message.GetBody<string>());
        }

        [Fact]
        public void when_sending_message_fails_transiently_once_then_retries()
        {
            var payload = Guid.NewGuid().ToString();

            var attempt = 0;
            var signal = new AutoResetEvent(false);
            var currentDelegate = sut.DoBeginSendMessageDelegate;
            sut.DoBeginSendMessageDelegate =
                (mf, ac) => {
                    if (attempt++ == 0) {
                        throw new TimeoutException();
                    }
                    currentDelegate(mf, ac);
                    signal.Set();
                };

            sut.SendAsync(() => new BrokeredMessage(payload));

            var message = subscriptionClient.Receive(TimeSpan.FromSeconds(5));
            Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)), "Test timed out");
            Assert.Equal(payload, message.GetBody<string>());
            Assert.Equal(2, attempt);
        }

        [Fact]
        public void when_sending_message_fails_transiently_multiple_times_then_fails()
        {
            var payload = Guid.NewGuid().ToString();

            var currentDelegate = sut.DoBeginSendMessageDelegate;
            sut.DoBeginSendMessageDelegate =
                (mf, ac) => { throw new TimeoutException(); };

            sut.SendAsync(() => new BrokeredMessage(payload));

            var message = subscriptionClient.Receive(TimeSpan.FromSeconds(5));
            Assert.Null(message);
        }
    }

    public class TestableTopicSender : TopicSender
    {
        public Action<BrokeredMessage, AsyncCallback> DoBeginSendMessageDelegate;

        public Action<IAsyncResult> DoEndSendMessageDelegate;

        public TestableTopicSender(ServiceBusSettings settings, string topic, RetryStrategy retryStrategy)
            : base(settings, topic, retryStrategy)
        {
            DoBeginSendMessageDelegate = base.DoBeginSendMessage;
            DoEndSendMessageDelegate = base.DoEndSendMessage;
        }

        protected override void DoBeginSendMessage(BrokeredMessage messageFactory, AsyncCallback ac)
        {
            DoBeginSendMessageDelegate(messageFactory, ac);
        }

        protected override void DoEndSendMessage(IAsyncResult ar)
        {
            DoEndSendMessageDelegate(ar);
        }
    }
}