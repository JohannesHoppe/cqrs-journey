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
using System.Linq;
using System.Threading;
using Infrastructure.Azure.Messaging;
using Infrastructure.Messaging;
using Infrastructure.Messaging.Handling;
using Infrastructure.Serialization;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.ServiceBus;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Moq;
using Xunit;

namespace Infrastructure.Azure.IntegrationTests.ServiceBusConfigFixture
{
    public class given_service_bus_config : IDisposable
    {
        private readonly NamespaceManager namespaceManager;

        private readonly RetryPolicy<ServiceBusTransientErrorDetectionStrategy> retryPolicy;

        private readonly ServiceBusSettings settings;

        private readonly ServiceBusConfig sut;

        public given_service_bus_config()
        {
            Trace.Listeners.Clear();
            settings = InfrastructureSettings.Read("Settings.xml").ServiceBus;
            foreach (var topic in settings.Topics) {
                topic.Path = topic.Path + Guid.NewGuid();
            }

            var tokenProvider = TokenProvider.CreateSharedSecretTokenProvider(settings.TokenIssuer, settings.TokenAccessKey);
            var serviceUri = ServiceBusEnvironment.CreateServiceUri(settings.ServiceUriScheme, settings.ServiceNamespace, settings.ServicePath);
            namespaceManager = new NamespaceManager(serviceUri, tokenProvider);

            var retryStrategy = new Incremental(3, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));
            retryPolicy = new RetryPolicy<ServiceBusTransientErrorDetectionStrategy>(retryStrategy);

            sut = new ServiceBusConfig(settings);

            Cleanup();
        }

        private void Cleanup()
        {
            foreach (var topic in settings.Topics) {
                retryPolicy.ExecuteAction(() => {
                    try {
                        namespaceManager.DeleteTopic(topic.Path);
                    } catch (MessagingEntityNotFoundException) { }
                });
            }
        }

        [Fact]
        public void when_initialized_then_creates_topics()
        {
            sut.Initialize();

            var topics = settings.Topics.Select(topic => new {
                    Topic = topic,
                    Description = retryPolicy.ExecuteAction(() => namespaceManager.GetTopic(topic.Path))
                })
                .ToList();

            Assert.False(topics.Any(tuple => tuple.Description == null));
        }

        [Fact]
        public void when_initialized_then_creates_subscriptions_with_filters()
        {
            sut.Initialize();

            var subscriptions = settings.Topics
                .SelectMany(topic => topic.Subscriptions.Select(subscription => new {Topic = topic, Subscription = subscription}))
                .Select(tuple => new {
                    tuple.Subscription,
                    Description = retryPolicy.ExecuteAction(() => namespaceManager.GetSubscription(tuple.Topic.Path, tuple.Subscription.Name)),
                    Rule = retryPolicy.ExecuteAction(() => namespaceManager.GetRules(tuple.Topic.Path, tuple.Subscription.Name).FirstOrDefault(x => x.Name == "Custom"))
                })
                .ToList();

            Assert.True(subscriptions.All(tuple => tuple.Description != null));
            Assert.True(subscriptions.All(tuple => tuple.Subscription.RequiresSession == tuple.Description.RequiresSession));
            Assert.True(subscriptions.All(tuple =>
                string.IsNullOrWhiteSpace(tuple.Subscription.SqlFilter) && tuple.Rule == null
                || !string.IsNullOrWhiteSpace(tuple.Subscription.SqlFilter) && ((SqlFilter) tuple.Rule.Filter).SqlExpression == tuple.Subscription.SqlFilter));
        }

        [Fact]
        public void when_initialized_then_subscriptions_updates_existing_filters()
        {
            settings.Topics = settings.Topics.Take(1).ToList();
            var topic = settings.Topics.First();
            topic.Subscriptions = topic.Subscriptions.Take(1).ToList();
            var subscription = topic.Subscriptions.First();
            subscription.SqlFilter = "TypeName='MyTypeA'";
            sut.Initialize();

            var rule = retryPolicy.ExecuteAction(() => namespaceManager.GetRules(topic.Path, subscription.Name).Single());
            Assert.Equal("TypeName='MyTypeA'", ((SqlFilter) rule.Filter).SqlExpression);

            subscription.SqlFilter = "TypeName='MyTypeB'";
            sut.Initialize();

            rule = retryPolicy.ExecuteAction(() => namespaceManager.GetRules(topic.Path, subscription.Name).Single());
            Assert.Equal("TypeName='MyTypeB'", ((SqlFilter) rule.Filter).SqlExpression);
        }

        [Fact]
        public void when_creating_processor_with_uninitialized_config_then_throws()
        {
            Assert.Throws<InvalidOperationException>(() => sut.CreateEventProcessor("all", Mock.Of<IEventHandler>(), Mock.Of<ITextSerializer>()));
        }

        [Fact]
        public void when_creating_processor_but_no_event_bus_topic_then_throws()
        {
            foreach (var topic in settings.Topics) {
                topic.IsEventBus = false;
            }
            sut.Initialize();

            Assert.Throws<ArgumentOutOfRangeException>(() => sut.CreateEventProcessor("all", Mock.Of<IEventHandler>(), Mock.Of<ITextSerializer>()));
        }

        [Fact]
        public void when_creating_processor_for_unconfigured_subscription_then_throws()
        {
            sut.Initialize();

            Assert.Throws<ArgumentOutOfRangeException>(() => sut.CreateEventProcessor("a", Mock.Of<IEventHandler>(), Mock.Of<ITextSerializer>()));
        }

        [Fact]
        public void when_creating_processor_then_receives_from_specified_subscription()
        {
            sut.Initialize();

            var waiter = new ManualResetEventSlim();
            var handler = new Mock<IEventHandler<AnEvent>>();
            var serializer = new JsonTextSerializer();
            var ev = new AnEvent();
            handler.Setup(x => x.Handle(It.IsAny<AnEvent>()))
                .Callback(() => waiter.Set());

            var processor = sut.CreateEventProcessor("log", handler.Object, serializer);

            processor.Start();

            var sender = new TopicSender(settings, settings.Topics.First(t => t.Path.StartsWith("conference/events")).Path);
            var bus = new EventBus(sender, new StandardMetadataProvider(), serializer);
            bus.Publish(ev);

            waiter.Wait(5000);

            handler.Verify(x => x.Handle(It.Is<AnEvent>(e => e.SourceId == ev.SourceId)));
        }

        [Fact]
        public void runs_migration_support_actions()
        {
            settings.Topics = settings.Topics.Take(1).ToList();
            var topic = settings.Topics.First();
            topic.Subscriptions = topic.Subscriptions.Take(1).ToList();
            topic.MigrationSupport.Clear();
            var subscription = topic.Subscriptions.First();
            subscription.SqlFilter = "TypeName='MyTypeA'";
            sut.Initialize();

            var rule = retryPolicy.ExecuteAction(() => namespaceManager.GetRules(topic.Path, subscription.Name).Single());
            Assert.Equal("TypeName='MyTypeA'", ((SqlFilter) rule.Filter).SqlExpression);

            topic.MigrationSupport.Add(new UpdateSubscriptionIfExists {Name = subscription.Name, SqlFilter = "1=0"});
            sut.Initialize();

            rule = retryPolicy.ExecuteAction(() => namespaceManager.GetRules(topic.Path, subscription.Name).Single());
            Assert.Equal("1=0", ((SqlFilter) rule.Filter).SqlExpression);
        }

        [Fact]
        public void migration_support_action_noops_if_subscription_does_not_exist()
        {
            settings.Topics = settings.Topics.Take(1).ToList();
            var topic = settings.Topics.First();
            topic.Subscriptions.Clear();
            topic.MigrationSupport.Clear();
            topic.MigrationSupport.Add(new UpdateSubscriptionIfExists {Name = "TestSubscription", SqlFilter = "1=0"});
            sut.Initialize();

            var subscriptions = retryPolicy.ExecuteAction(() => namespaceManager.GetSubscriptions(topic.Path)).ToList();
            Assert.Equal(0, subscriptions.Count);
        }

        public void Dispose()
        {
            Cleanup();
        }

        public class AnEvent : IEvent
        {
            public AnEvent()
            {
                SourceId = Guid.NewGuid();
            }

            public Guid SourceId { get; set; }
        }
    }
}