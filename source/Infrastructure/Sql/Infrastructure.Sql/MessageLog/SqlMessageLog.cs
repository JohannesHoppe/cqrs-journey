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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Infrastructure.MessageLog;
using Infrastructure.Messaging;
using Infrastructure.Serialization;

namespace Infrastructure.Sql.MessageLog
{
    public class SqlMessageLog : IEventLogReader
    {
        private readonly IMetadataProvider metadataProvider;

        private readonly string nameOrConnectionString;

        private readonly ITextSerializer serializer;

        public SqlMessageLog(string nameOrConnectionString, ITextSerializer serializer, IMetadataProvider metadataProvider)
        {
            this.nameOrConnectionString = nameOrConnectionString;
            this.serializer = serializer;
            this.metadataProvider = metadataProvider;
        }

        public void Save(IEvent @event)
        {
            using (var context = new MessageLogDbContext(nameOrConnectionString)) {
                var metadata = metadataProvider.GetMetadata(@event);

                context.Set<MessageLogEntity>()
                    .Add(new MessageLogEntity {
                        Id = Guid.NewGuid(),
                        SourceId = @event.SourceId.ToString(),
                        Kind = metadata.TryGetValue(StandardMetadata.Kind),
                        AssemblyName = metadata.TryGetValue(StandardMetadata.AssemblyName),
                        FullName = metadata.TryGetValue(StandardMetadata.FullName),
                        Namespace = metadata.TryGetValue(StandardMetadata.Namespace),
                        TypeName = metadata.TryGetValue(StandardMetadata.TypeName),
                        SourceType = metadata.TryGetValue(StandardMetadata.SourceType),
                        CreationDate = DateTime.UtcNow.ToString("o"),
                        Payload = serializer.Serialize(@event)
                    });
                context.SaveChanges();
            }
        }

        public void Save(ICommand command)
        {
            using (var context = new MessageLogDbContext(nameOrConnectionString)) {
                var metadata = metadataProvider.GetMetadata(command);

                context.Set<MessageLogEntity>()
                    .Add(new MessageLogEntity {
                        Id = Guid.NewGuid(),
                        SourceId = command.Id.ToString(),
                        Kind = metadata.TryGetValue(StandardMetadata.Kind),
                        AssemblyName = metadata.TryGetValue(StandardMetadata.AssemblyName),
                        FullName = metadata.TryGetValue(StandardMetadata.FullName),
                        Namespace = metadata.TryGetValue(StandardMetadata.Namespace),
                        TypeName = metadata.TryGetValue(StandardMetadata.TypeName),
                        SourceType = metadata.TryGetValue(StandardMetadata.SourceType),
                        CreationDate = DateTime.UtcNow.ToString("o"),
                        Payload = serializer.Serialize(command)
                    });
                context.SaveChanges();
            }
        }

        public IEnumerable<IEvent> Query(QueryCriteria criteria)
        {
            return new SqlQuery(nameOrConnectionString, serializer, criteria);
        }

        private class SqlQuery : IEnumerable<IEvent>
        {
            private readonly QueryCriteria criteria;

            private readonly string nameOrConnectionString;

            private readonly ITextSerializer serializer;

            public SqlQuery(string nameOrConnectionString, ITextSerializer serializer, QueryCriteria criteria)
            {
                this.nameOrConnectionString = nameOrConnectionString;
                this.serializer = serializer;
                this.criteria = criteria;
            }

            public IEnumerator<IEvent> GetEnumerator()
            {
                return new DisposingEnumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private class DisposingEnumerator : IEnumerator<IEvent>
            {
                private MessageLogDbContext context;

                private IEnumerator<IEvent> events;

                private readonly SqlQuery sqlQuery;

                public DisposingEnumerator(SqlQuery sqlQuery)
                {
                    this.sqlQuery = sqlQuery;
                }

                ~DisposingEnumerator()
                {
                    if (context != null) {
                        context.Dispose();
                    }
                }

                public void Dispose()
                {
                    if (context != null) {
                        context.Dispose();
                        context = null;
                        GC.SuppressFinalize(this);
                    }
                    if (events != null) {
                        events.Dispose();
                    }
                }

                public IEvent Current {
                    get { return events.Current; }
                }

                object IEnumerator.Current {
                    get { return Current; }
                }

                public bool MoveNext()
                {
                    if (context == null) {
                        context = new MessageLogDbContext(sqlQuery.nameOrConnectionString);
                        var queryable = context.Set<MessageLogEntity>()
                            .AsQueryable()
                            .Where(x => x.Kind == StandardMetadata.EventKind);

                        var where = sqlQuery.criteria.ToExpression();
                        if (where != null) {
                            queryable = queryable.Where(where);
                        }

                        events = queryable
                            .AsEnumerable()
                            .Select(x => sqlQuery.serializer.Deserialize<IEvent>(x.Payload))
                            .GetEnumerator();
                    }

                    return events.MoveNext();
                }

                public void Reset()
                {
                    throw new NotSupportedException();
                }
            }
        }
    }
}