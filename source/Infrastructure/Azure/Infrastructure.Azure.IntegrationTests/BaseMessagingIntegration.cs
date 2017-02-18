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
using Infrastructure.Azure.Messaging;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.ServiceBus;
using Microsoft.Practices.TransientFaultHandling;

namespace Infrastructure.Azure.IntegrationTests
{
    /// <summary>
    ///     Base class for messaging integration tests.
    /// </summary>
    public class given_messaging_settings
    {
        public ServiceBusSettings Settings { get; }

        public given_messaging_settings()
        {
            Trace.Listeners.Clear();
            Settings = InfrastructureSettings.Read("Settings.xml").ServiceBus;
        }
    }

    public class given_a_topic_and_subscription : given_messaging_settings, IDisposable
    {
        private readonly RetryPolicy<ServiceBusTransientErrorDetectionStrategy> retryPolicy;

        public string Topic { get; }

        public string Subscription { get; }

        public given_a_topic_and_subscription()
        {
            Trace.Listeners.Clear();

            Topic = "cqrsjourney-test-" + Guid.NewGuid();
            Subscription = "test-" + Guid.NewGuid();

            var retryStrategy = new Incremental(3, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));
            retryPolicy = new RetryPolicy<ServiceBusTransientErrorDetectionStrategy>(retryStrategy);

            // Creates the topic too.
            retryPolicy.ExecuteAction(() => Settings.CreateSubscription(Topic, Subscription));
        }

        public virtual void Dispose()
        {
            // Deletes subscriptions too.
            retryPolicy.ExecuteAction(() => Settings.TryDeleteTopic(Topic));
        }
    }
}