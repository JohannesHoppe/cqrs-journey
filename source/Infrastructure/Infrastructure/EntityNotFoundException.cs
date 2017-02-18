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
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace Infrastructure
{
    [Serializable]
    public class EntityNotFoundException : Exception
    {
        public Guid EntityId { get; }

        public string EntityType { get; }

        public EntityNotFoundException() { }

        public EntityNotFoundException(Guid entityId) : base(entityId.ToString())
        {
            EntityId = entityId;
        }

        public EntityNotFoundException(Guid entityId, string entityType)
            : base(entityType + ": " + entityId)
        {
            EntityId = entityId;
            EntityType = entityType;
        }

        public EntityNotFoundException(Guid entityId, string entityType, string message, Exception inner)
            : base(message, inner)
        {
            EntityId = entityId;
            EntityType = entityType;
        }

        protected EntityNotFoundException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
            if (info == null) {
                throw new ArgumentNullException("info");
            }

            EntityId = Guid.Parse(info.GetString("entityId"));
            EntityType = info.GetString("entityType");
        }

        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("entityId", EntityId.ToString());
            info.AddValue("entityType", EntityType);
        }
    }
}