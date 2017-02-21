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
using System.Diagnostics;
using System.Threading;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.ServiceBus;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using RetryPolicy = Microsoft.Practices.TransientFaultHandling.RetryPolicy;

namespace Infrastructure.Azure.Messaging
{
    /// <summary>
    ///     Implements an asynchronous sender of messages to a Windows Azure Service Bus topic.
    /// </summary>
    public class TopicSender : IMessageSender
    {
        private readonly RetryPolicy retryPolicy;

        private readonly Uri serviceUri;

        private readonly ServiceBusSettings settings;

        private readonly TokenProvider tokenProvider;

        private readonly string topic;

        private readonly TopicClient topicClient;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TopicSender" /> class,
        ///     automatically creating the given topic if it does not exist.
        /// </summary>
        public TopicSender(ServiceBusSettings settings, string topic)
            : this(settings, topic, new ExponentialBackoff(10, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1))) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="TopicSender" /> class,
        ///     automatically creating the given topic if it does not exist.
        /// </summary>
        protected TopicSender(ServiceBusSettings settings, string topic, RetryStrategy retryStrategy)
        {
            this.settings = settings;
            this.topic = topic;

            tokenProvider = TokenProvider.CreateSharedSecretTokenProvider(settings.TokenIssuer, settings.TokenAccessKey);
            serviceUri = ServiceBusEnvironment.CreateServiceUri(settings.ServiceUriScheme, settings.ServiceNamespace, settings.ServicePath);

            // TODO: This could be injected.
            retryPolicy = new RetryPolicy<ServiceBusTransientErrorDetectionStrategy>(retryStrategy);
            retryPolicy.Retrying +=
                (s, e) => {
                    var handler = Retrying;
                    if (handler != null) {
                        handler(this, EventArgs.Empty);
                    }

                    Trace.TraceWarning("An error occurred in attempt number {1} to send a message: {0}", e.LastException.Message, e.CurrentRetryCount);
                };

            var factory = MessagingFactory.Create(serviceUri, tokenProvider);
            topicClient = factory.CreateTopicClient(this.topic);
        }

        public void SendAsync(IEnumerable<Func<BrokeredMessage>> messageFactories)
        {
            // TODO: batch/transactional sending?
            foreach (var messageFactory in messageFactories) {
                SendAsync(messageFactory);
            }
        }

        protected virtual void DoBeginSendMessage(BrokeredMessage message, AsyncCallback ac)
        {
            try {
                topicClient.BeginSend(message, ac, message);
            } catch {
                message.Dispose();
                throw;
            }
        }

        protected virtual void DoEndSendMessage(IAsyncResult ar)
        {
            try {
                topicClient.EndSend(ar);
            } finally {
                using (ar.AsyncState as IDisposable) { }
            }
        }

        /// <summary>
        ///     Notifies that the sender is retrying due to a transient fault.
        /// </summary>
        public event EventHandler Retrying;

        /// <summary>
        ///     Asynchronously sends the specified message.
        /// </summary>
        public void SendAsync(Func<BrokeredMessage> messageFactory)
        {
            // TODO: SendAsync is not currently being used by the app or infrastructure.
            // Consider removing or have a callback notifying the result.
            // Always send async.
            SendAsync(messageFactory, () => { }, ex => { });
        }

        public void SendAsync(Func<BrokeredMessage> messageFactory, Action successCallback, Action<Exception> exceptionCallback)
        {
            retryPolicy.ExecuteAction(
                ac => DoBeginSendMessage(messageFactory(), ac),
                DoEndSendMessage,
                successCallback,
                ex => {
                    Trace.TraceError("An unrecoverable error occurred while trying to send a message:\r\n{0}", ex);
                    exceptionCallback(ex);
                });
        }

        public void Send(Func<BrokeredMessage> messageFactory)
        {
            var resetEvent = new ManualResetEvent(false);
            Exception exception = null;

            SendAsync(
                messageFactory,
                () => resetEvent.Set(),
                ex => {
                    exception = ex;
                    resetEvent.Set();
                });

            resetEvent.WaitOne();
            if (exception != null) {
                throw exception;
            }
        }
    }
}