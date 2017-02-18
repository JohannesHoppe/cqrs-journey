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
using Infrastructure.Utils;

namespace Payments
{
    public class ThidPartyProcessorPaymentItem
    {
        public Guid Id { get; }

        public string Description { get; }

        public decimal Amount { get; }

        public ThidPartyProcessorPaymentItem(string description, decimal amount)
        {
            Id = GuidUtil.NewSequentialId();

            Description = description;
            Amount = amount;
        }

        protected ThidPartyProcessorPaymentItem() { }
    }
}