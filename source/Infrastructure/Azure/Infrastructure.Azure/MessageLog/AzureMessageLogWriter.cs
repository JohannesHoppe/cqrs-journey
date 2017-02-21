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
using System.Data.Services.Client;
using System.Net;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.AzureStorage;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;

namespace Infrastructure.Azure.MessageLog
{
    public class AzureMessageLogWriter : IAzureMessageLogWriter
    {
        private readonly CloudStorageAccount account;

        private readonly CloudTableClient tableClient;

        private readonly string tableName;

        private readonly RetryPolicy retryPolicy;

        public AzureMessageLogWriter(CloudStorageAccount account, string tableName)
        {
            if (account == null) {
                throw new ArgumentNullException(nameof(account));
            }
            if (tableName == null) {
                throw new ArgumentNullException(nameof(tableName));
            }
            if (string.IsNullOrWhiteSpace(tableName)) {
                throw new ArgumentException("tableName");
            }

            this.account = account;
            this.tableName = tableName;
            tableClient = account.CreateCloudTableClient();
            tableClient.DefaultRequestOptions.RetryPolicy = new NoRetry();

            var retryStrategy = new ExponentialBackoff(10, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));
            retryPolicy = new RetryPolicy<StorageTransientErrorDetectionStrategy>(retryStrategy);

            var tableReference = retryPolicy.ExecuteAction(() => tableClient.GetTableReference(tableName));
            retryPolicy.ExecuteAction(() => tableReference.CreateIfNotExistsAsync());
        }

        public void Save(MessageLogEntity entity)
        {
            retryPolicy.ExecuteAction(() => {
                var context = tableClient.GetTableServiceContext();

                context.AddObject(tableName, entity);

                try {
                    context.SaveChanges();
                } catch (DataServiceRequestException dsre) {
                    var clientException = dsre.InnerException as DataServiceClientException;
                    // If we get a conflict, we ignore it as we've already saved the message, 
                    // making this log idempotent.
                    if (clientException == null || clientException.StatusCode != (int) HttpStatusCode.Conflict) {
                        throw;
                    }
                }
            });
        }
    }
}