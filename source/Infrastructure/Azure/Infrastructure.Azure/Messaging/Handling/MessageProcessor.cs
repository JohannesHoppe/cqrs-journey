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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using Infrastructure.Serialization;
using Microsoft.ServiceBus.Messaging;

namespace Infrastructure.Azure.Messaging.Handling
{
    /// <summary>
    ///     Provides basic common processing code for components that handle
    ///     incoming messages from a receiver.
    /// </summary>
    public abstract class MessageProcessor : IProcessor, IDisposable
    {
        private const int MaxProcessingRetries = 5;

        private readonly object lockObject = new object();

        private readonly IMessageReceiver receiver;

        private bool disposed;

        private bool started;

        protected ITextSerializer Serializer { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageProcessor" /> class.
        /// </summary>
        protected MessageProcessor(IMessageReceiver receiver, ITextSerializer serializer)
        {
            this.receiver = receiver;
            Serializer = serializer;
        }

        /// <summary>
        ///     Processes the message.
        /// </summary>
        /// <param name="traceIdentifier">The identifier that can be used to track the source message in the logs.</param>
        /// <param name="payload">The typed message payload.</param>
        /// <param name="messageId">The message id.</param>
        /// <param name="correlationId">The message correlation id.</param>
        protected abstract void ProcessMessage(string traceIdentifier, object payload, string messageId, string correlationId);

        /// <summary>
        ///     Disposes the resources used by the processor.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed) {
                if (disposing) {
                    Stop();
                    disposed = true;

                    using (receiver as IDisposable) {
                        // Dispose receiver if it's disposable.
                    }
                }
            }
        }

        ~MessageProcessor()
        {
            Dispose(false);
        }

        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification = "By design.")]
        private MessageReleaseAction OnMessageReceived(BrokeredMessage message)
        {
            // NOTE: type information does not belong here. It's a responsibility 
            // of the serializer to be self-contained and put any information it 
            // might need for rehydration.

            object payload;
            using (var stream = message.GetBody<Stream>()) {
                using (var reader = new StreamReader(stream)) {
                    try {
                        payload = Serializer.Deserialize(reader);
                    } catch (SerializationException e) {
                        return MessageReleaseAction.DeadLetterMessage(e.Message, e.ToString());
                    }
                }
            }

            // TODO: have a better trace correlation mechanism (that is used in both the sender and receiver).
            var traceIdentifier = BuildTraceIdentifier(message);

            try {
                ProcessMessage(traceIdentifier, payload, message.MessageId, message.CorrelationId);
            } catch (Exception e) {
                return HandleProcessingException(message, traceIdentifier, e);
            }

            return CompleteMessage(message, traceIdentifier);
        }

        private MessageReleaseAction CompleteMessage(BrokeredMessage message, string traceIdentifier)
        {
            // Trace.WriteLine("The message" + traceIdentifier + " has been processed and will be completed.");
            return MessageReleaseAction.CompleteMessage;
        }

        private MessageReleaseAction HandleProcessingException(BrokeredMessage message, string traceIdentifier, Exception e)
        {
            if (message.DeliveryCount > MaxProcessingRetries) {
                Trace.TraceError("An error occurred while processing the message" + traceIdentifier + " and will be dead-lettered:\r\n{0}", e);
                return MessageReleaseAction.DeadLetterMessage(e.Message, e.ToString());
            }
            Trace.TraceWarning("An error occurred while processing the message" + traceIdentifier + " and will be abandoned:\r\n{0}", e);
            return MessageReleaseAction.AbandonMessage;
        }

        // TODO: remove once we have a better trace correlation mechanism (that is used in both the sender and receiver).
        private static string BuildTraceIdentifier(BrokeredMessage message)
        {
            try {
                var messageId = message.MessageId;
                return string.Format(CultureInfo.InvariantCulture, " (MessageId: {0})", messageId);
            } catch (ObjectDisposedException) {
                // if there is any kind of exception trying to build a trace identifier, ignore, as it is not important.
            }

            return string.Empty;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) {
                throw new ObjectDisposedException("MessageProcessor");
            }
        }

        /// <summary>
        ///     Disposes the resources used by the processor.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Starts the listener.
        /// </summary>
        public virtual void Start()
        {
            ThrowIfDisposed();
            lock (lockObject) {
                if (!started) {
                    receiver.Start(OnMessageReceived);
                    started = true;
                }
            }
        }

        /// <summary>
        ///     Stops the listener.
        /// </summary>
        public virtual void Stop()
        {
            lock (lockObject) {
                if (started) {
                    receiver.Stop();
                    started = false;
                }
            }
        }
    }
}