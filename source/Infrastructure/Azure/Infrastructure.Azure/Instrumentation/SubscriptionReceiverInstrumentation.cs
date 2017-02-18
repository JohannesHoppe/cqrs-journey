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

namespace Infrastructure.Azure.Instrumentation
{
    public class SubscriptionReceiverInstrumentation : ISubscriptionReceiverInstrumentation, IDisposable
    {
        public const string TotalMessagesCounterName = "Total messages received";

        public const string TotalMessagesSuccessfullyProcessedCounterName = "Total messages processed";

        public const string TotalMessagesUnsuccessfullyProcessedCounterName = "Total messages processed (fails)";

        public const string TotalMessagesCompletedCounterName = "Total messages completed";

        public const string TotalMessagesNotCompletedCounterName = "Total messages not completed";

        public const string MessagesReceivedPerSecondCounterName = "Messages received/sec";

        public const string AverageMessageProcessingTimeCounterName = "Avg. message processing time";

        public const string AverageMessageProcessingTimeBaseCounterName = "Avg. message processing time base";

        public const string CurrentMessagesInProcessCounterName = "Current messages";

        private readonly PerformanceCounter averageMessageProcessingTimeBaseCounter;

        private readonly PerformanceCounter averageMessageProcessingTimeCounter;

        private readonly PerformanceCounter currentMessagesInProcessCounter;

        private readonly PerformanceCounter messagesReceivedPerSecondCounter;

        private readonly PerformanceCounter totalMessagesCompletedCounter;

        private readonly PerformanceCounter totalMessagesCounter;

        private readonly PerformanceCounter totalMessagesNotCompletedCounter;

        private readonly PerformanceCounter totalMessagesSuccessfullyProcessedCounter;

        private readonly PerformanceCounter totalMessagesUnsuccessfullyProcessedCounter;

        protected string InstanceName { get; }

        protected bool InstrumentationEnabled { get; }

        public SubscriptionReceiverInstrumentation(string instanceName, bool instrumentationEnabled)
        {
            InstanceName = instanceName;
            InstrumentationEnabled = instrumentationEnabled;

            if (InstrumentationEnabled) {
                totalMessagesCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, TotalMessagesCounterName, InstanceName, false);
                totalMessagesSuccessfullyProcessedCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, TotalMessagesSuccessfullyProcessedCounterName, InstanceName, false);
                totalMessagesUnsuccessfullyProcessedCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, TotalMessagesUnsuccessfullyProcessedCounterName, InstanceName,
                    false);
                totalMessagesCompletedCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, TotalMessagesCompletedCounterName, InstanceName, false);
                totalMessagesNotCompletedCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, TotalMessagesNotCompletedCounterName, InstanceName, false);
                messagesReceivedPerSecondCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, MessagesReceivedPerSecondCounterName, InstanceName, false);
                averageMessageProcessingTimeCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, AverageMessageProcessingTimeCounterName, InstanceName, false);
                averageMessageProcessingTimeBaseCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, AverageMessageProcessingTimeBaseCounterName, InstanceName, false);
                currentMessagesInProcessCounter = new PerformanceCounter(Constants.ReceiversPerformanceCountersCategory, CurrentMessagesInProcessCounterName, InstanceName, false);

                totalMessagesCounter.RawValue = 0;
                totalMessagesSuccessfullyProcessedCounter.RawValue = 0;
                totalMessagesUnsuccessfullyProcessedCounter.RawValue = 0;
                totalMessagesCompletedCounter.RawValue = 0;
                totalMessagesNotCompletedCounter.RawValue = 0;
                averageMessageProcessingTimeCounter.RawValue = 0;
                averageMessageProcessingTimeBaseCounter.RawValue = 0;
                currentMessagesInProcessCounter.RawValue = 0;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) {
                if (InstrumentationEnabled) {
                    totalMessagesCounter.Dispose();
                    totalMessagesSuccessfullyProcessedCounter.Dispose();
                    totalMessagesUnsuccessfullyProcessedCounter.Dispose();
                    totalMessagesCompletedCounter.Dispose();
                    totalMessagesNotCompletedCounter.Dispose();
                    messagesReceivedPerSecondCounter.Dispose();
                    averageMessageProcessingTimeCounter.Dispose();
                    averageMessageProcessingTimeBaseCounter.Dispose();
                    currentMessagesInProcessCounter.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void MessageReceived()
        {
            if (InstrumentationEnabled) {
                try {
                    totalMessagesCounter.Increment();
                    messagesReceivedPerSecondCounter.Increment();
                    currentMessagesInProcessCounter.Increment();
                } catch (ObjectDisposedException) { }
            }
        }

        public void MessageProcessed(bool success, long elapsedMilliseconds)
        {
            if (InstrumentationEnabled) {
                try {
                    if (success) {
                        totalMessagesSuccessfullyProcessedCounter.Increment();
                    } else {
                        totalMessagesUnsuccessfullyProcessedCounter.Increment();
                    }

                    averageMessageProcessingTimeCounter.IncrementBy(elapsedMilliseconds / 100);
                    averageMessageProcessingTimeBaseCounter.Increment();
                } catch (ObjectDisposedException) { }
            }
        }

        public void MessageCompleted(bool success)
        {
            if (InstrumentationEnabled) {
                try {
                    if (success) {
                        totalMessagesCompletedCounter.Increment();
                    } else {
                        totalMessagesNotCompletedCounter.Increment();
                    }
                    currentMessagesInProcessCounter.Decrement();
                } catch (ObjectDisposedException) { }
            }
        }
    }
}