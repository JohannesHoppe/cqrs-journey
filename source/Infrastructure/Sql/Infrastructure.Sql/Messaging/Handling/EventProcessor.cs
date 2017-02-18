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

using Infrastructure.Messaging;
using Infrastructure.Messaging.Handling;
using Infrastructure.Serialization;

namespace Infrastructure.Sql.Messaging.Handling
{
    /// <summary>
    ///     Processes incoming events from the bus and routes them to the appropriate
    ///     handlers.
    /// </summary>
    public class EventProcessor : MessageProcessor, IEventHandlerRegistry
    {
        private readonly EventDispatcher messageDispatcher;

        public EventProcessor(IMessageReceiver receiver, ITextSerializer serializer)
            : base(receiver, serializer)
        {
            messageDispatcher = new EventDispatcher();
        }

        protected override void ProcessMessage(object payload, string correlationId)
        {
            var @event = (IEvent) payload;
            messageDispatcher.DispatchMessage(@event, null, correlationId, "");
        }

        public void Register(IEventHandler eventHandler)
        {
            messageDispatcher.Register(eventHandler);
        }
    }
}