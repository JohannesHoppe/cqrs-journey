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

using System;
using System.Diagnostics;
using System.IO;
using Infrastructure.BlobStorage;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.AzureStorage;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

namespace Infrastructure.Azure.BlobStorage
{
    public class CloudBlobStorage : IBlobStorage
    {
        private readonly CloudStorageAccount account;

        private readonly CloudBlobClient blobClient;

        private readonly RetryPolicy<StorageTransientErrorDetectionStrategy> readRetryPolicy;

        private readonly string rootContainerName;

        private readonly RetryPolicy<StorageTransientErrorDetectionStrategy> writeRetryPolicy;

        public CloudBlobStorage(CloudStorageAccount account, string rootContainerName)
        {
            this.account = account;
            this.rootContainerName = rootContainerName;

            blobClient = account.CreateCloudBlobClient();
            blobClient.DefaultRequestOptions.RetryPolicy = new NoRetry();

            readRetryPolicy = new RetryPolicy<StorageTransientErrorDetectionStrategy>(new Incremental(1, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
            readRetryPolicy.Retrying += (s, e) => Trace.TraceWarning("An error occurred in attempt number {1} to read from blob storage: {0}", e.LastException.Message, e.CurrentRetryCount);
            writeRetryPolicy = new RetryPolicy<StorageTransientErrorDetectionStrategy>(new FixedInterval(1, TimeSpan.FromSeconds(10)) {FastFirstRetry = false});
            writeRetryPolicy.Retrying += (s, e) => Trace.TraceWarning("An error occurred in attempt number {1} to write to blob storage: {0}", e.LastException.Message, e.CurrentRetryCount);

            var containerReference = blobClient.GetContainerReference(this.rootContainerName);
            writeRetryPolicy.ExecuteAction(() => containerReference.CreateIfNotExists());
        }

        public byte[] Find(string id)
        {
            var containerReference = blobClient.GetContainerReference(rootContainerName);
            var blobReference = containerReference.GetBlobReference(id);

            return readRetryPolicy.ExecuteAction(() => {
                using (var stream = new MemoryStream()) {
                    blobReference.DownloadToStream(stream);
                    return stream.GetBuffer();
                }
            });
        }

        public void Save(string id, string contentType, byte[] blob)
        {
            var client = account.CreateCloudBlobClient();
            var containerReference = client.GetContainerReference(rootContainerName);

            var blobReference = containerReference.GetBlockBlobReference(id);

            writeRetryPolicy.ExecuteAction(() => {
                blobReference.UploadFromByteArray(blob, 0, blob.Length);
            });
        }

        public void Delete(string id)
        {
            var client = account.CreateCloudBlobClient();
            var containerReference = client.GetContainerReference(rootContainerName);
            var blobReference = containerReference.GetBlobReference(id);

            writeRetryPolicy.ExecuteAction(() => {
                blobReference.DeleteIfExists();
            });
        }
    }
}