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
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Transactions;

namespace Infrastructure.Sql.Messaging.Implementation
{
    public class MessageSender : IMessageSender
    {
        private readonly IDbConnectionFactory connectionFactory;

        private const string InsertQuery = "INSERT INTO @Table (Body, DeliveryDate, CorrelationId) VALUES (@Body, @DeliveryDate, @CorrelationId)";

        private readonly string name;

        private readonly string tableName;

        public MessageSender(IDbConnectionFactory connectionFactory, string name, string tableName)
        {
            this.connectionFactory = connectionFactory;
            this.name = name;
            this.tableName = tableName;
        }

        private void InsertMessage(Message message, DbConnection connection)
        {
            using (var command = new SqlCommand(InsertQuery, (SqlConnection) connection)) {
                var tableNameParameter = new SqlParameter();
                tableNameParameter.ParameterName = "@Table";
                tableNameParameter.Value = tableName;
                command.Parameters.Add(tableNameParameter);
                command.Parameters.Add("@Body", SqlDbType.NVarChar).Value = message.Body;
                command.Parameters.Add("@DeliveryDate", SqlDbType.DateTime).Value = message.DeliveryDate.HasValue ? (object) message.DeliveryDate.Value : DBNull.Value;
                command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar).Value = (object) message.CorrelationId ?? DBNull.Value;

                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        ///     Sends the specified message.
        /// </summary>
        public void Send(Message message)
        {
            using (var connection = connectionFactory.CreateConnection(name)) {
                connection.Open();

                InsertMessage(message, connection);
            }
        }

        /// <summary>
        ///     Sends a batch of messages.
        /// </summary>
        public void Send(IEnumerable<Message> messages)
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required)) {
                using (var connection = connectionFactory.CreateConnection(name)) {
                    connection.Open();

                    foreach (var message in messages) {
                        InsertMessage(message, connection);
                    }
                }

                scope.Complete();
            }
        }
    }
}