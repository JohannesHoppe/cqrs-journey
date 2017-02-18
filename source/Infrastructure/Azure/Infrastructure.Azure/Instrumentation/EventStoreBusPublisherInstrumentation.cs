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
    public class EventStoreBusPublisherInstrumentation : IEventStoreBusPublisherInstrumentation, IDisposable
    {
        public const string CurrentEventPublishersCounterName = "Event publishers";

        public const string TotalEventsPublishingRequestsCounterName = "Total events publishing requested";

        public const string EventPublishingRequestsPerSecondCounterName = "Event publishing requests/sec";

        public const string TotalEventsPublishedCounterName = "Total events published";

        public const string EventsPublishedPerSecondCounterName = "Events published/sec";

        private readonly PerformanceCounter currentEventPublishersCounter;

        private readonly PerformanceCounter eventPublishingRequestsPerSecondCounter;

        private readonly PerformanceCounter eventsPublishedPerSecondCounter;

        private readonly bool instrumentationEnabled;

        private readonly PerformanceCounter totalEventsPublishedCounter;

        private readonly PerformanceCounter totalEventsPublishingRequestedCounter;

        public EventStoreBusPublisherInstrumentation(string instanceName, bool instrumentationEnabled)
        {
            this.instrumentationEnabled = instrumentationEnabled;

            if (this.instrumentationEnabled) {
                currentEventPublishersCounter = new PerformanceCounter(Constants.EventPublishersPerformanceCountersCategory, CurrentEventPublishersCounterName, instanceName, false);
                totalEventsPublishingRequestedCounter = new PerformanceCounter(Constants.EventPublishersPerformanceCountersCategory, TotalEventsPublishingRequestsCounterName, instanceName, false);
                eventPublishingRequestsPerSecondCounter = new PerformanceCounter(Constants.EventPublishersPerformanceCountersCategory, EventPublishingRequestsPerSecondCounterName, instanceName,
                    false);
                totalEventsPublishedCounter = new PerformanceCounter(Constants.EventPublishersPerformanceCountersCategory, TotalEventsPublishedCounterName, instanceName, false);
                eventsPublishedPerSecondCounter = new PerformanceCounter(Constants.EventPublishersPerformanceCountersCategory, EventsPublishedPerSecondCounterName, instanceName, false);

                currentEventPublishersCounter.RawValue = 0;
                totalEventsPublishingRequestedCounter.RawValue = 0;
                eventPublishingRequestsPerSecondCounter.RawValue = 0;
                totalEventsPublishedCounter.RawValue = 0;
                eventsPublishedPerSecondCounter.RawValue = 0;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) {
                if (instrumentationEnabled) {
                    currentEventPublishersCounter.Dispose();
                    totalEventsPublishingRequestedCounter.Dispose();
                    eventPublishingRequestsPerSecondCounter.Dispose();
                    eventsPublishedPerSecondCounter.Dispose();
                    totalEventsPublishedCounter.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void EventsPublishingRequested(int eventCount)
        {
            if (instrumentationEnabled) {
                totalEventsPublishingRequestedCounter.IncrementBy(eventCount);
                eventPublishingRequestsPerSecondCounter.IncrementBy(eventCount);
            }
        }

        public void EventPublished()
        {
            if (instrumentationEnabled) {
                totalEventsPublishedCounter.Increment();
                eventsPublishedPerSecondCounter.Increment();
            }
        }

        public void EventPublisherStarted()
        {
            if (instrumentationEnabled) {
                currentEventPublishersCounter.Increment();
            }
        }

        public void EventPublisherFinished()
        {
            if (instrumentationEnabled) {
                currentEventPublishersCounter.Decrement();
            }
        }
    }
}