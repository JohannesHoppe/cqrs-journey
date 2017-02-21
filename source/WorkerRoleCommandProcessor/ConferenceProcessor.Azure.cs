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

using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using Conference;
using Infrastructure;
using Infrastructure.Azure;
using Infrastructure.Azure.BlobStorage;
using Infrastructure.Azure.EventSourcing;
using Infrastructure.Azure.Instrumentation;
using Infrastructure.Azure.MessageLog;
using Infrastructure.Azure.Messaging;
using Infrastructure.Azure.Messaging.Handling;
using Infrastructure.BlobStorage;
using Infrastructure.EventSourcing;
using Infrastructure.Messaging;
using Infrastructure.Messaging.Handling;
using Infrastructure.Serialization;
using Microsoft.Practices.Unity;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Registration;
using Registration.Handlers;
using Order = Registration.Order;

namespace WorkerRoleCommandProcessor
{
    /// <summary>
    ///     Windows Azure side of the processor, which is included for compilation conditionally
    ///     at the csproj level.
    /// </summary>
    /// <devdoc>
    ///     NOTE: this file is only compiled on non-DebugLocal configurations. In DebugLocal
    ///     you will not see full syntax coloring, IntelliSense, etc.. But it is still
    ///     much more readable and usable than a grayed-out piece of code inside an #if
    /// </devdoc>
    partial class ConferenceProcessor
    {
        private InfrastructureSettings azureSettings;

        private ServiceBusConfig busConfig;

        partial void OnCreating()
        {
            azureSettings = InfrastructureSettings.Read("Settings.xml");
            busConfig = new ServiceBusConfig(azureSettings.ServiceBus);

            busConfig.Initialize();
        }

        partial void OnCreateContainer(UnityContainer container)
        {
            var metadata = container.Resolve<IMetadataProvider>();
            var serializer = container.Resolve<ITextSerializer>();

            // blob
            var blobStorageAccount = CloudStorageAccount.Parse(azureSettings.BlobStorage.ConnectionString);
            container.RegisterInstance<IBlobStorage>(new CloudBlobStorage(blobStorageAccount, azureSettings.BlobStorage.RootContainerName));

            var commandBus = new CommandBus(new TopicSender(azureSettings.ServiceBus, Topics.Commands.Path), metadata, serializer);
            var eventsTopicSender = new TopicSender(azureSettings.ServiceBus, Topics.Events.Path);
            container.RegisterInstance<IMessageSender>("events", eventsTopicSender);
            container.RegisterInstance<IMessageSender>("orders", new TopicSender(azureSettings.ServiceBus, Topics.EventsOrders.Path));
            container.RegisterInstance<IMessageSender>("seatsavailability", new TopicSender(azureSettings.ServiceBus, Topics.EventsAvailability.Path));
            var eventBus = new EventBus(eventsTopicSender, metadata, serializer);

            var sessionlessCommandProcessor = new CommandProcessor(new SubscriptionReceiver(azureSettings.ServiceBus, Topics.Commands.Path, Topics.Commands.Subscriptions.Sessionless, false), serializer);
            var seatsAvailabilityCommandProcessor = new CommandProcessor(new SubscriptionReceiver(azureSettings.ServiceBus, Topics.Commands.Path, Topics.Commands.Subscriptions.Seatsavailability, false), serializer);

            var synchronousCommandBus = new SynchronousCommandBusDecorator(commandBus);
            container.RegisterInstance<ICommandBus>(synchronousCommandBus);

            container.RegisterInstance<IEventBus>(eventBus);
            container.RegisterInstance<IProcessor>("SessionlessCommandProcessor", sessionlessCommandProcessor);
            container.RegisterInstance<IProcessor>("SeatsAvailabilityCommandProcessor", seatsAvailabilityCommandProcessor);

            RegisterRepositories(container);
            RegisterEventProcessors(container);
            RegisterCommandHandlers(container, sessionlessCommandProcessor, seatsAvailabilityCommandProcessor);

            // handle order commands inline, as they do not have competition.
            synchronousCommandBus.Register(container.Resolve<ICommandHandler>("OrderCommandHandler"));

            // message log
            var messageLogAccount = CloudStorageAccount.Parse(azureSettings.MessageLog.ConnectionString);

            container.RegisterInstance<IProcessor>("EventLogger", new AzureMessageLogListener(
                new AzureMessageLogWriter(messageLogAccount, azureSettings.MessageLog.TableName),
                new SubscriptionReceiver(azureSettings.ServiceBus, Topics.Events.Path, Topics.Events.Subscriptions.Log)));

            container.RegisterInstance<IProcessor>("OrderEventLogger", new AzureMessageLogListener(
                new AzureMessageLogWriter(messageLogAccount, azureSettings.MessageLog.TableName),
                new SubscriptionReceiver(azureSettings.ServiceBus, Topics.EventsOrders.Path, Topics.EventsOrders.Subscriptions.LogOrders)));

            container.RegisterInstance<IProcessor>("SeatsAvailabilityEventLogger", new AzureMessageLogListener(
                new AzureMessageLogWriter(messageLogAccount, azureSettings.MessageLog.TableName),
                new SubscriptionReceiver(azureSettings.ServiceBus, Topics.EventsAvailability.Path, Topics.EventsAvailability.Subscriptions.LogAvail)));

            container.RegisterInstance<IProcessor>("CommandLogger", new AzureMessageLogListener(
                new AzureMessageLogWriter(messageLogAccount, azureSettings.MessageLog.TableName),
                new SubscriptionReceiver(azureSettings.ServiceBus, Topics.Commands.Path, Topics.Commands.Subscriptions.Log)));
        }

        private void RegisterEventProcessors(UnityContainer container)
        {
            container.RegisterType<RegistrationProcessManagerRouter>(new ContainerControlledLifetimeManager());

            container.RegisterEventProcessor<RegistrationProcessManagerRouter>(busConfig, Topics.Events.Subscriptions.RegistrationPMNextSteps, instrumentationEnabled);
            container.RegisterEventProcessor<PricedOrderViewModelGenerator>(busConfig, Topics.Events.Subscriptions.PricedOrderViewModelGeneratorV3, instrumentationEnabled);
            container.RegisterEventProcessor<ConferenceViewModelGenerator>(busConfig, Topics.Events.Subscriptions.ConferenceViewModelGenerator, instrumentationEnabled);

            container.RegisterEventProcessor<RegistrationProcessManagerRouter>(busConfig, Topics.EventsOrders.Subscriptions.RegistrationPMOrderPlacedOrders, instrumentationEnabled);
            container.RegisterEventProcessor<RegistrationProcessManagerRouter>(busConfig, Topics.EventsOrders.Subscriptions.RegistrationPMNextStepsOrders, instrumentationEnabled);
            container.RegisterEventProcessor<DraftOrderViewModelGenerator>(busConfig, Topics.EventsOrders.Subscriptions.OrderViewModelGeneratorOrders, instrumentationEnabled);
            container.RegisterEventProcessor<PricedOrderViewModelGenerator>(busConfig, Topics.EventsOrders.Subscriptions.PricedOrderViewModelOrders, instrumentationEnabled);
            container.RegisterEventProcessor<SeatAssignmentsViewModelGenerator>(busConfig, Topics.EventsOrders.Subscriptions.SeatAssignmentsViewModelOrders, instrumentationEnabled);
            container.RegisterEventProcessor<SeatAssignmentsHandler>(busConfig, Topics.EventsOrders.Subscriptions.SeatAssignmentsHandlerOrders, instrumentationEnabled);
            container.RegisterEventProcessor<OrderEventHandler>(busConfig, Topics.EventsOrders.Subscriptions.OrderEventHandlerOrders, instrumentationEnabled);

            container.RegisterEventProcessor<RegistrationProcessManagerRouter>(busConfig, Topics.EventsAvailability.Subscriptions.RegistrationPMNextStepsAvail, instrumentationEnabled);
            container.RegisterEventProcessor<ConferenceViewModelGenerator>(busConfig, Topics.EventsAvailability.Subscriptions.ConferenceViewModelAvail, instrumentationEnabled);
        }

        private static void RegisterCommandHandlers(IUnityContainer unityContainer, ICommandHandlerRegistry sessionlessRegistry, ICommandHandlerRegistry seatsAvailabilityRegistry)
        {
            var commandHandlers = unityContainer.ResolveAll<ICommandHandler>().ToList();
            var seatsAvailabilityHandler = commandHandlers.First(x => x.GetType().IsAssignableFrom(typeof(SeatsAvailabilityHandler)));

            seatsAvailabilityRegistry.Register(seatsAvailabilityHandler);
            foreach (var commandHandler in commandHandlers.Where(x => x != seatsAvailabilityHandler)) {
                sessionlessRegistry.Register(commandHandler);
            }
        }

        private void RegisterRepositories(UnityContainer container)
        {
            // repository
            var eventSourcingAccount = CloudStorageAccount.Parse(azureSettings.EventSourcing.ConnectionString);
            var ordersEventStore = new EventStore(eventSourcingAccount, azureSettings.EventSourcing.OrdersTableName);
            var seatsAvailabilityEventStore = new EventStore(eventSourcingAccount, azureSettings.EventSourcing.SeatsAvailabilityTableName);

            container.RegisterInstance<IEventStore>("orders", ordersEventStore);
            container.RegisterInstance<IPendingEventsQueue>("orders", ordersEventStore);

            container.RegisterInstance<IEventStore>("seatsavailability", seatsAvailabilityEventStore);
            container.RegisterInstance<IPendingEventsQueue>("seatsavailability", seatsAvailabilityEventStore);

            container.RegisterType<IEventStoreBusPublisher, EventStoreBusPublisher>(
                "orders",
                new ContainerControlledLifetimeManager(),
                new InjectionConstructor(
                    new ResolvedParameter<IMessageSender>("orders"),
                    new ResolvedParameter<IPendingEventsQueue>("orders"),
                    new EventStoreBusPublisherInstrumentation("worker - orders", instrumentationEnabled)));
            container.RegisterType<IEventStoreBusPublisher, EventStoreBusPublisher>(
                "seatsavailability",
                new ContainerControlledLifetimeManager(),
                new InjectionConstructor(
                    new ResolvedParameter<IMessageSender>("seatsavailability"),
                    new ResolvedParameter<IPendingEventsQueue>("seatsavailability"),
                    new EventStoreBusPublisherInstrumentation("worker - seatsavailability", instrumentationEnabled)));

            var cache = new MemoryCache("RepositoryCache");

            container.RegisterType<IEventSourcedRepository<Order>, AzureEventSourcedRepository<Order>>(
                new ContainerControlledLifetimeManager(),
                new InjectionConstructor(
                    new ResolvedParameter<IEventStore>("orders"),
                    new ResolvedParameter<IEventStoreBusPublisher>("orders"),
                    typeof(ITextSerializer),
                    typeof(IMetadataProvider),
                    cache));

            container.RegisterType<IEventSourcedRepository<SeatAssignments>, AzureEventSourcedRepository<SeatAssignments>>(
                new ContainerControlledLifetimeManager(),
                new InjectionConstructor(
                    new ResolvedParameter<IEventStore>("orders"),
                    new ResolvedParameter<IEventStoreBusPublisher>("orders"),
                    typeof(ITextSerializer),
                    typeof(IMetadataProvider),
                    cache));

            container.RegisterType<IEventSourcedRepository<SeatsAvailability>, AzureEventSourcedRepository<SeatsAvailability>>(
                new ContainerControlledLifetimeManager(),
                new InjectionConstructor(
                    new ResolvedParameter<IEventStore>("seatsavailability"),
                    new ResolvedParameter<IEventStoreBusPublisher>("seatsavailability"),
                    typeof(ITextSerializer),
                    typeof(IMetadataProvider),
                    cache));

            // to satisfy the IProcessor requirements.
            container.RegisterInstance<IProcessor>(
                "OrdersEventStoreBusPublisher",
                new PublisherProcessorAdapter(container.Resolve<IEventStoreBusPublisher>("orders"), cancellationTokenSource.Token));
            container.RegisterInstance<IProcessor>(
                "SeatsAvailabilityEventStoreBusPublisher",
                new PublisherProcessorAdapter(container.Resolve<IEventStoreBusPublisher>("seatsavailability"), cancellationTokenSource.Token));
        }

        // to satisfy the IProcessor requirements.
        // TODO: we should unify and probably use token-based Start only processors.
        private class PublisherProcessorAdapter : IProcessor
        {
            private readonly IEventStoreBusPublisher publisher;

            private readonly CancellationToken token;

            public PublisherProcessorAdapter(IEventStoreBusPublisher publisher, CancellationToken token)
            {
                this.publisher = publisher;
                this.token = token;
            }

            public void Start()
            {
                publisher.Start(token);
            }

            public void Stop()
            {
                // Do nothing. The cancelled token will stop the process anyway.
            }
        }
    }
}