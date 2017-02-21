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

namespace Infrastructure.Azure.Messaging
{
    /// <summary>
    ///     Sepecfies how the <see cref="Microsoft.ServiceBus.Messaging.BrokeredMessage" /> should be released.
    /// </summary>
    public class MessageReleaseAction
    {
        public static readonly MessageReleaseAction CompleteMessage = new MessageReleaseAction(MessageReleaseActionKind.Complete);

        public static readonly MessageReleaseAction AbandonMessage = new MessageReleaseAction(MessageReleaseActionKind.Abandon);

        public MessageReleaseActionKind Kind { get; }

        public string DeadLetterReason { get; private set; }

        public string DeadLetterDescription { get; private set; }

        protected MessageReleaseAction(MessageReleaseActionKind kind)
        {
            Kind = kind;
        }

        public static MessageReleaseAction DeadLetterMessage(string reason, string description)
        {
            return new MessageReleaseAction(MessageReleaseActionKind.DeadLetter) {
                DeadLetterReason = reason,
                DeadLetterDescription = description
            };
        }
    }

    public enum MessageReleaseActionKind
    {
        Complete,

        Abandon,

        DeadLetter
    }
}