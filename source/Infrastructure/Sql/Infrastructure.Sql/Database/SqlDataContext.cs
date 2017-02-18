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
using System.Data;
using System.Data.Entity;
using Infrastructure.Database;
using Infrastructure.Messaging;

namespace Infrastructure.Sql.Database
{
    public class SqlDataContext<T> : IDataContext<T>
        where T : class, IAggregateRoot
    {
        private readonly DbContext context;

        private readonly IEventBus eventBus;

        public SqlDataContext(Func<DbContext> contextFactory, IEventBus eventBus)
        {
            this.eventBus = eventBus;
            context = contextFactory.Invoke();
        }

        ~SqlDataContext()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) {
                context.Dispose();
            }
        }

        public T Find(Guid id)
        {
            return context.Set<T>().Find(id);
        }

        public void Save(T aggregateRoot)
        {
            var entry = context.Entry(aggregateRoot);

            if (entry.State == EntityState.Detached) {
                context.Set<T>().Add(aggregateRoot);
            }

            // Can't have transactions across storage and message bus.
            context.SaveChanges();

            var eventPublisher = aggregateRoot as IEventPublisher;
            if (eventPublisher != null) {
                eventBus.Publish(eventPublisher.Events);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}