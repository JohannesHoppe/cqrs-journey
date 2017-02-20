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
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Sql.Messaging.Implementation
{
    public class MessageReceiver : IMessageReceiver, IDisposable
    {
        private readonly IDbConnectionFactory connectionFactory;

        private readonly object lockObject = new object();

        private readonly string name;

        private readonly string tableName;

        private readonly TimeSpan pollDelay;

        private const string ReadQuery = "SELECT TOP (1) "
            + "t.[Id] AS [Id], "
            + "t.[Body] AS [Body], "
            + "t.[DeliveryDate] AS [DeliveryDate], "
            + "t.[CorrelationId] AS [CorrelationId] "
            + "FROM @Table t WITH (UPDLOCK, READPAST) "
            + "WHERE (t.[DeliveryDate] IS NULL) OR (t.[DeliveryDate] <= @CurrentDate) "
            + "ORDER BY t.[Id] ASC";

        private const string DeleteQuery = "DELETE FROM {0} WHERE Id = @Id";

        private CancellationTokenSource cancellationSource;

        public MessageReceiver(IDbConnectionFactory connectionFactory, string name, string tableName)
            : this(connectionFactory, name, tableName, TimeSpan.FromMilliseconds(100)) { }

        public MessageReceiver(IDbConnectionFactory connectionFactory, string name, string tableName, TimeSpan pollDelay)
        {
            this.connectionFactory = connectionFactory;
            this.name = name;
            this.tableName = tableName;
            this.pollDelay = pollDelay;
        }

        protected virtual void Dispose(bool disposing)
        {
            Stop();
        }

        ~MessageReceiver()
        {
            Dispose(false);
        }

        /// <summary>
        ///     Receives the messages in an endless loop.
        /// </summary>
        private void ReceiveMessages(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested) {
                if (!ReceiveMessage()) {
                    Thread.Sleep(pollDelay);
                }
            }
        }

        protected bool ReceiveMessage()
        {
            var tableParameter = new SqlParameter {
                ParameterName = "@Table",
                Value = tableName
            };
            using (var connection = connectionFactory.CreateConnection(name)) {
                var currentDate = GetCurrentDate();

                connection.Open();
                using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted)) {
                    try {
                        long messageId;
                        Message message;

                        using (var command = connection.CreateCommand()) {
                            command.Transaction = transaction;
                            command.CommandType = CommandType.Text;
                            command.CommandText = ReadQuery;
                            ((SqlCommand) command).Parameters.Add("@CurrentDate", SqlDbType.DateTime).Value = currentDate;
                            command.Parameters.Add(tableParameter);

                            using (var reader = command.ExecuteReader()) {
                                if (!reader.Read()) {
                                    return false;
                                }

                                var body = (string) reader["Body"];
                                var deliveryDateValue = reader["DeliveryDate"];
                                var deliveryDate = deliveryDateValue == DBNull.Value ? null : (DateTime?) deliveryDateValue;
                                var correlationIdValue = reader["CorrelationId"];
                                var correlationId = (string) (correlationIdValue == DBNull.Value ? null : correlationIdValue);

                                message = new Message(body, deliveryDate, correlationId);
                                messageId = (long) reader["Id"];
                            }
                        }

                        MessageReceived(this, new MessageReceivedEventArgs(message));

                        using (var command = connection.CreateCommand()) {
                            command.Transaction = transaction;
                            command.CommandType = CommandType.Text;
                            command.CommandText = DeleteQuery;
                            ((SqlCommand) command).Parameters.Add("@Id", SqlDbType.BigInt).Value = messageId;
                            command.Parameters.Add(tableParameter);

                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    } catch (Exception) {
                        try {
                            transaction.Rollback();
                        } catch { }
                        throw;
                    }
                }
            }

            return true;
        }

        protected virtual DateTime GetCurrentDate()
        {
            return DateTime.UtcNow;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event EventHandler<MessageReceivedEventArgs> MessageReceived = (sender, args) => { };

        public void Start()
        {
            lock (lockObject) {
                if (cancellationSource == null) {
                    cancellationSource = new CancellationTokenSource();
                    Task.Factory.StartNew(
                        () => ReceiveMessages(cancellationSource.Token),
                        cancellationSource.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Current);
                }
            }
        }

        public void Stop()
        {
            lock (lockObject) {
                using (cancellationSource) {
                    if (cancellationSource != null) {
                        cancellationSource.Cancel();
                        cancellationSource = null;
                    }
                }
            }
        }
    }
}