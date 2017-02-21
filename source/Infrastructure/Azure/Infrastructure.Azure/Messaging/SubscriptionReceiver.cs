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

// Based on http://windowsazurecat.com/2011/09/best-practices-leveraging-windows-azure-service-bus-brokered-messaging-api/

using System;
using System.Threading;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Infrastructure.Azure.Messaging
{
    /// <summary>
    ///     Implements an asynchronous receiver of messages from a Windows Azure
    ///     Service Bus topic subscription.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In V3 we made a lot of changes to optimize the performance and scalability of the receiver.
    ///         See <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258557"> Journey chapter 7</see> for more information
    ///         on the optimizations and migration to V3.
    ///     </para>
    ///     <para>
    ///         The current implementation uses async calls to communicate with the service bus, although the message
    ///         processing is done with a blocking synchronous call.
    ///         We could still make several performance improvements. For example, we could react to system-wide throttling
    ///         indicators to avoid overwhelming
    ///         the services when under heavy load. See
    ///         <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258557"> Journey chapter 7</see> for more potential
    ///         performance and scalability optimizations.
    ///     </para>
    /// </remarks>
    public class SubscriptionReceiver : IMessageReceiver, IDisposable
    {
        private readonly Func<SubscriptionClient> clientFactory;

        private readonly object lockObject = new object();

        private CancellationTokenSource cancellationSource;

        /// <summary>
        ///     Handler for incoming messages. The return value indicates whether the message should be disposed.
        /// </summary>
        protected Func<BrokeredMessage, MessageReleaseAction> MessageHandler { get; private set; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubscriptionReceiver" /> class,
        ///     automatically creating the topic and subscription if they don't exist.
        /// </summary>
        public SubscriptionReceiver(ServiceBusSettings settings, string topic, string subscription)
            : this(settings, topic, subscription, false, new RetryExponential(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(15), 10)) {}

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubscriptionReceiver" /> class,
        ///     automatically creating the topic and subscription if they don't exist.
        /// </summary>
        public SubscriptionReceiver(ServiceBusSettings settings, string topic, string subscription, bool processInParallel)
            : this(settings, topic, subscription, processInParallel, new RetryExponential(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(15), 10)) {}

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubscriptionReceiver" /> class,
        ///     automatically creating the topic and subscription if they don't exist.
        /// </summary>
        protected SubscriptionReceiver(ServiceBusSettings settings, string topic, string subscription, bool processInParallel,
            RetryPolicy retryPolicy)
        {
            clientFactory = () => {
                var tokenProvider = TokenProvider.CreateSharedSecretTokenProvider(settings.TokenIssuer, settings.TokenAccessKey);
                var serviceUri = ServiceBusEnvironment.CreateServiceUri(settings.ServiceUriScheme, settings.ServiceNamespace, settings.ServicePath);
                var messagingFactory = MessagingFactory.Create(serviceUri, tokenProvider);
                var client = messagingFactory.CreateSubscriptionClient(topic, subscription);
                client.PrefetchCount = processInParallel ? 18 : 14;
                client.RetryPolicy = retryPolicy;
                return client;
            };
        }

        protected virtual void Dispose(bool disposing)
        {
            Stop();
        }

        protected virtual MessageReleaseAction InvokeMessageHandler(BrokeredMessage message)
        {
            return MessageHandler != null ? MessageHandler(message) : MessageReleaseAction.AbandonMessage;
        }

        ~SubscriptionReceiver()
        {
            Dispose(false);
        }

        /// <summary>
        ///     Receives the messages in an endless asynchronous loop.
        /// </summary>
        private void ReceiveMessages(CancellationToken cancellationToken)
        {
            var client = clientFactory();
            client.OnMessage(message => {
                var action = MessageHandler(message);
                switch (action.Kind) {
                    case MessageReleaseActionKind.Complete:
                        message.Complete();
                        break;
                    case MessageReleaseActionKind.Abandon:
                        message.Abandon();
                        break;
                    case MessageReleaseActionKind.DeadLetter:
                        message.DeadLetter(action.DeadLetterReason, action.DeadLetterDescription);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            });
            cancellationToken.Register(() => client.Close());
        }

        /// <summary>
        ///     Stops the listener if it was started previously.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Starts the listener.
        /// </summary>
        public void Start(Func<BrokeredMessage, MessageReleaseAction> messageHandler)
        {
            lock (lockObject) {
                MessageHandler = messageHandler;
                cancellationSource = new CancellationTokenSource();
                ReceiveMessages(cancellationSource.Token);
            }
        }

        /// <summary>
        ///     Stops the listener.
        /// </summary>
        public void Stop()
        {
            lock (lockObject) {
                using (cancellationSource) {
                    if (cancellationSource != null) {
                        cancellationSource.Cancel();
                        cancellationSource = null;
                        MessageHandler = null;
                    }
                }
            }
        }
    }
}