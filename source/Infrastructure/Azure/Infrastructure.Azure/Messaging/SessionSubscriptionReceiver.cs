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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.Azure.Instrumentation;
using Infrastructure.Azure.Utils;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.ServiceBus;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Infrastructure.Azure.Messaging
{
    /// <summary>
    ///     Implements an asynchronous receiver of messages from a Windows Azure
    ///     Service Bus topic subscription using sessions.
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
    ///         We could still make several performance improvements. For example, we could take advantage of sessions and
    ///         batch multiple messages to avoid accessing the
    ///         repositories multiple times where appropriate. See
    ///         <see cref="http://go.microsoft.com/fwlink/p/?LinkID=258557"> Journey chapter 7</see> for more potential
    ///         performance and scalability optimizations.
    ///     </para>
    /// </remarks>
    public class SessionSubscriptionReceiver : IMessageReceiver, IDisposable
    {
        private static readonly TimeSpan AcceptSessionLongPollingTimeout = TimeSpan.FromMinutes(1);

        private readonly DynamicThrottling dynamicThrottling;

        private readonly ISessionSubscriptionReceiverInstrumentation instrumentation;

        private readonly object lockObject = new object();

        private readonly RetryPolicy receiveRetryPolicy;

        private readonly bool requiresSequentialProcessing;

        private readonly Uri serviceUri;

        private readonly ServiceBusSettings settings;

        private readonly string subscription;

        private readonly TokenProvider tokenProvider;

        private readonly string topic;

        private CancellationTokenSource cancellationSource;

        private readonly SubscriptionClient client;

        /// <summary>
        ///     Handler for incoming messages. The return value indicates whether the message should be disposed.
        /// </summary>
        protected Func<BrokeredMessage, MessageReleaseAction> MessageHandler { get; private set; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubscriptionReceiver" /> class,
        ///     automatically creating the topic and subscription if they don't exist.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Instrumentation disabled in this overload")]
        public SessionSubscriptionReceiver(ServiceBusSettings settings, string topic, string subscription, bool requiresSequentialProcessing = true)
            : this(
                settings,
                topic,
                subscription,
                requiresSequentialProcessing,
                new SessionSubscriptionReceiverInstrumentation(subscription, false),
                new ExponentialBackoff(10, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1))) { }

        public SessionSubscriptionReceiver(ServiceBusSettings settings, string topic, string subscription, bool requiresSequentialProcessing,
            ISessionSubscriptionReceiverInstrumentation instrumentation)
            : this(
                settings,
                topic,
                subscription,
                requiresSequentialProcessing,
                instrumentation,
                new ExponentialBackoff(10, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1))) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SessionSubscriptionReceiver" /> class,
        ///     automatically creating the topic and subscription if they don't exist.
        /// </summary>
        protected SessionSubscriptionReceiver(ServiceBusSettings settings, string topic, string subscription, bool requiresSequentialProcessing,
            ISessionSubscriptionReceiverInstrumentation instrumentation, RetryStrategy backgroundRetryStrategy)
        {
            this.settings = settings;
            this.topic = topic;
            this.subscription = subscription;
            this.requiresSequentialProcessing = requiresSequentialProcessing;
            this.instrumentation = instrumentation;

            tokenProvider = TokenProvider.CreateSharedSecretTokenProvider(settings.TokenIssuer, settings.TokenAccessKey);
            serviceUri = ServiceBusEnvironment.CreateServiceUri(settings.ServiceUriScheme, settings.ServiceNamespace, settings.ServicePath);

            var messagingFactory = MessagingFactory.Create(serviceUri, tokenProvider);
            client = messagingFactory.CreateSubscriptionClient(topic, subscription);
            if (this.requiresSequentialProcessing) {
                client.PrefetchCount = 10;
            } else {
                client.PrefetchCount = 15;
            }

            dynamicThrottling =
                new DynamicThrottling(
                    160,
                    30,
                    3,
                    5,
                    1,
                    10000);
            receiveRetryPolicy = new RetryPolicy<ServiceBusTransientErrorDetectionStrategy>(backgroundRetryStrategy);
            receiveRetryPolicy.Retrying += (s, e) => {
                dynamicThrottling.Penalize();
                Trace.TraceWarning(
                    "An error occurred in attempt number {1} to receive a message from subscription {2}: {0}",
                    e.LastException.Message,
                    e.CurrentRetryCount,
                    this.subscription);
            };
        }

        protected virtual void Dispose(bool disposing)
        {
            Stop();

            if (disposing) {
                using (instrumentation as IDisposable) { }
                using (dynamicThrottling) { }
            }
        }

        protected virtual MessageReleaseAction InvokeMessageHandler(BrokeredMessage message)
        {
            return MessageHandler != null ? MessageHandler(message) : MessageReleaseAction.AbandonMessage;
        }

        ~SessionSubscriptionReceiver()
        {
            Dispose(false);
        }

        private void AcceptSession(CancellationToken cancellationToken)
        {
            dynamicThrottling.WaitUntilAllowedParallelism(cancellationToken);

            if (!cancellationToken.IsCancellationRequested) {
                // Initialize a custom action acting as a callback whenever a non-transient exception occurs while accepting a session.
                Action<Exception> recoverAcceptSession = ex => {
                    // Just log an exception. Do not allow an unhandled exception to terminate the message receive loop abnormally.
                    Trace.TraceError("An unrecoverable error occurred while trying to accept a session in subscription {1}:\r\n{0}", ex, subscription);
                    dynamicThrottling.Penalize();

                    if (!cancellationToken.IsCancellationRequested) {
                        // Continue accepting new sessions until told to stop regardless of any exceptions.
                        TaskEx.Delay(10000).ContinueWith(t => AcceptSession(cancellationToken));
                    }
                };

                receiveRetryPolicy.ExecuteAction(
                    cb => client.BeginAcceptMessageSession(AcceptSessionLongPollingTimeout, cb, null),
                    ar => {
                        // Complete the asynchronous operation. This may throw an exception that will be handled internally by retry policy.
                        try {
                            return client.EndAcceptMessageSession(ar);
                        } catch (TimeoutException) {
                            // TimeoutException is not just transient but completely expected in this case, so not relying on Topaz to retry
                            return null;
                        }
                    },
                    session => {
                        if (session != null) {
                            instrumentation.SessionStarted();
                            dynamicThrottling.NotifyWorkStarted();
                            // starts a new task to process new sessions in parallel when enough threads are available
                            Task.Factory.StartNew(() => AcceptSession(cancellationToken), cancellationToken);
                            ReceiveMessagesAndCloseSession(session, cancellationToken);
                        } else {
                            AcceptSession(cancellationToken);
                        }
                    },
                    recoverAcceptSession);
            }
        }

        /// <summary>
        ///     Receives the messages in an asynchronous loop and closes the session once there are no more messages.
        /// </summary>
        private void ReceiveMessagesAndCloseSession(MessageSession session, CancellationToken cancellationToken)
        {
            var unreleasedMessages = new CountdownEvent(1);

            Action<bool> closeSession = success => {
                Action doClose = () => {
                    try {
                        unreleasedMessages.Signal();
                        if (!unreleasedMessages.Wait(15000, cancellationToken)) {
                            Trace.TraceWarning("Waited for pending unreleased messages before closing session in subscription {0} but they did not complete in time", subscription);
                        }
                    } catch (OperationCanceledException) { } finally {
                        unreleasedMessages.Dispose();
                    }

                    receiveRetryPolicy.ExecuteAction(
                        cb => session.BeginClose(cb, null),
                        session.EndClose,
                        () => {
                            instrumentation.SessionEnded();
                            if (success) {
                                dynamicThrottling.NotifyWorkCompleted();
                            } else {
                                dynamicThrottling.NotifyWorkCompletedWithError();
                            }
                        },
                        ex => {
                            instrumentation.SessionEnded();
                            Trace.TraceError("An unrecoverable error occurred while trying to close a session in subscription {1}:\r\n{0}", ex, subscription);
                            dynamicThrottling.NotifyWorkCompletedWithError();
                        });
                };

                if (requiresSequentialProcessing) {
                    doClose.Invoke();
                } else {
                    // Allow some time for releasing the messages before closing. Also, continue in a non I/O completion thread in order to block.
                    TaskEx.Delay(200).ContinueWith(t => doClose());
                }
            };

            // Declare an action to receive the next message in the queue or closes the session if cancelled.
            Action receiveNext = null;

            // Declare an action acting as a callback whenever a non-transient exception occurs while receiving or processing messages.
            Action<Exception> recoverReceive = null;

            // Declare an action responsible for the core operations in the message receive loop.
            Action receiveMessage = () => {
                // Use a retry policy to execute the Receive action in an asynchronous and reliable fashion.
                receiveRetryPolicy.ExecuteAction
                (
                    cb => {
                        // Start receiving a new message asynchronously.
                        // Does not wait for new messages to arrive in a session. If no further messages we will just close the session.
                        session.BeginReceive(TimeSpan.Zero, cb, null);
                    },
                    // Complete the asynchronous operation. This may throw an exception that will be handled internally by retry policy.
                    session.EndReceive,
                    msg => {
                        // Process the message once it was successfully received
                        // Check if we actually received any messages.
                        if (msg != null) {
                            var roundtripStopwatch = Stopwatch.StartNew();
                            long schedulingElapsedMilliseconds = 0;
                            long processingElapsedMilliseconds = 0;

                            unreleasedMessages.AddCount();

                            Task.Factory.StartNew(() => {
                                var releaseAction = MessageReleaseAction.AbandonMessage;

                                try {
                                    instrumentation.MessageReceived();

                                    schedulingElapsedMilliseconds = roundtripStopwatch.ElapsedMilliseconds;

                                    // Make sure the process was told to stop receiving while it was waiting for a new message.
                                    if (!cancellationToken.IsCancellationRequested) {
                                        try {
                                            try {
                                                // Process the received message.
                                                releaseAction = InvokeMessageHandler(msg);

                                                processingElapsedMilliseconds = roundtripStopwatch.ElapsedMilliseconds - schedulingElapsedMilliseconds;
                                                instrumentation.MessageProcessed(releaseAction.Kind == MessageReleaseActionKind.Complete, processingElapsedMilliseconds);
                                            } catch {
                                                processingElapsedMilliseconds = roundtripStopwatch.ElapsedMilliseconds - schedulingElapsedMilliseconds;
                                                instrumentation.MessageProcessed(false, processingElapsedMilliseconds);

                                                throw;
                                            }
                                        } finally {
                                            if (roundtripStopwatch.Elapsed > TimeSpan.FromSeconds(45)) {
                                                dynamicThrottling.Penalize();
                                            }
                                        }
                                    }
                                } finally {
                                    // Ensure that any resources allocated by a BrokeredMessage instance are released.
                                    if (requiresSequentialProcessing) {
                                        ReleaseMessage(msg, releaseAction, () => { receiveNext(); }, () => { closeSession(false); }, unreleasedMessages, processingElapsedMilliseconds,
                                            schedulingElapsedMilliseconds, roundtripStopwatch);
                                    } else {
                                        // Receives next without waiting for the message to be released.
                                        ReleaseMessage(msg, releaseAction, () => { }, () => { dynamicThrottling.Penalize(); }, unreleasedMessages, processingElapsedMilliseconds,
                                            schedulingElapsedMilliseconds, roundtripStopwatch);
                                        receiveNext.Invoke();
                                    }
                                }
                            });
                        } else {
                            // no more messages in the session, close it and do not continue receiving
                            closeSession(true);
                        }
                    },
                    ex => {
                        // Invoke a custom action to indicate that we have encountered an exception and
                        // need further decision as to whether to continue receiving messages.
                        recoverReceive.Invoke(ex);
                    });
            };

            // Initialize an action to receive the next message in the queue or closes the session if cancelled.
            receiveNext = () => {
                if (!cancellationToken.IsCancellationRequested) {
                    // Continue receiving and processing new messages until told to stop.
                    receiveMessage.Invoke();
                } else {
                    closeSession(true);
                }
            };

            // Initialize a custom action acting as a callback whenever a non-transient exception occurs while receiving or processing messages.
            recoverReceive = ex => {
                // Just log an exception. Do not allow an unhandled exception to terminate the message receive loop abnormally.
                Trace.TraceError("An unrecoverable error occurred while trying to receive a new message from subscription {1}:\r\n{0}", ex, subscription);

                // Cannot continue to receive messages from this session.
                closeSession(false);
            };

            // Start receiving messages asynchronously for the session.
            receiveNext.Invoke();
        }

        private void ReleaseMessage(BrokeredMessage msg, MessageReleaseAction releaseAction, Action completeReceive, Action onReleaseError, CountdownEvent countdown,
            long processingElapsedMilliseconds, long schedulingElapsedMilliseconds, Stopwatch roundtripStopwatch)
        {
            switch (releaseAction.Kind) {
                case MessageReleaseActionKind.Complete:
                    msg.SafeCompleteAsync(
                        subscription,
                        operationSucceeded => {
                            msg.Dispose();
                            OnMessageCompleted(operationSucceeded, countdown);
                            if (operationSucceeded) {
                                completeReceive();
                            } else {
                                onReleaseError();
                            }
                        },
                        processingElapsedMilliseconds,
                        schedulingElapsedMilliseconds,
                        roundtripStopwatch);
                    break;
                case MessageReleaseActionKind.Abandon:
                    dynamicThrottling.Penalize();
                    msg.SafeAbandonAsync(
                        subscription,
                        operationSucceeded => {
                            msg.Dispose();
                            OnMessageCompleted(false, countdown);

                            onReleaseError();
                        },
                        processingElapsedMilliseconds,
                        schedulingElapsedMilliseconds,
                        roundtripStopwatch);
                    break;
                case MessageReleaseActionKind.DeadLetter:
                    dynamicThrottling.Penalize();
                    msg.SafeDeadLetterAsync(
                        subscription,
                        releaseAction.DeadLetterReason,
                        releaseAction.DeadLetterDescription,
                        operationSucceeded => {
                            msg.Dispose();
                            OnMessageCompleted(false, countdown);

                            if (operationSucceeded) {
                                completeReceive();
                            } else {
                                onReleaseError();
                            }
                        },
                        processingElapsedMilliseconds,
                        schedulingElapsedMilliseconds,
                        roundtripStopwatch);
                    break;
                default:
                    break;
            }
        }

        private void OnMessageCompleted(bool success, CountdownEvent countdown)
        {
            instrumentation.MessageCompleted(success);
            try {
                countdown.Signal();
            } catch (ObjectDisposedException) {
                // It could happen in a rare case that due to a timing issue between closing the session and disposing the countdown,
                // that the countdown is already disposed. This is OK and it can continue processing normally.
            }
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
                // If it's not null, there is already a listening task.
                if (cancellationSource == null) {
                    MessageHandler = messageHandler;
                    cancellationSource = new CancellationTokenSource();
                    Task.Factory.StartNew(() => AcceptSession(cancellationSource.Token), cancellationSource.Token);
                    dynamicThrottling.Start(cancellationSource.Token);
                }
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