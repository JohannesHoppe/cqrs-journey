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
using System.Text;
using Infrastructure.Azure.BlobStorage;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Xunit;

namespace Infrastructure.Azure.IntegrationTests.Storage.BlobStorageFixture
{
    public class given_blob_storage : IDisposable
    {
        protected readonly CloudStorageAccount account;

        protected readonly string rootContainerName;

        protected readonly CloudBlobStorage sut;

        public given_blob_storage()
        {
            var settings = InfrastructureSettings.Read("Settings.xml").BlobStorage;
            account = CloudStorageAccount.Parse(settings.ConnectionString);
            rootContainerName = Guid.NewGuid().ToString();
            sut = new CloudBlobStorage(account, rootContainerName);
        }

        public void Dispose()
        {
            var client = account.CreateCloudBlobClient();
            var containerReference = client.GetContainerReference(rootContainerName);

            try {
                containerReference.Delete();
            } catch (StorageClientException) { }
        }
    }

    public class when_retrieving_from_non_existing_container : given_blob_storage
    {
        private readonly byte[] bytes;

        public when_retrieving_from_non_existing_container()
        {
            bytes = sut.Find(Guid.NewGuid().ToString());
        }

        [Fact]
        public void then_returns_null()
        {
            Assert.Null(bytes);
        }
    }

    public class given_blob_storage_with_existing_root_container : IDisposable
    {
        protected readonly CloudStorageAccount account;

        protected readonly string rootContainerName;

        protected readonly CloudBlobStorage sut;

        public given_blob_storage_with_existing_root_container()
        {
            var settings = InfrastructureSettings.Read("Settings.xml").BlobStorage;
            account = CloudStorageAccount.Parse(settings.ConnectionString);
            rootContainerName = Guid.NewGuid().ToString();

            var client = account.CreateCloudBlobClient();
            var containerReference = client.GetContainerReference(rootContainerName);

            containerReference.Create();

            sut = new CloudBlobStorage(account, rootContainerName);
        }

        public void Dispose()
        {
            var client = account.CreateCloudBlobClient();
            var containerReference = client.GetContainerReference(rootContainerName);

            try {
                containerReference.Delete();
            } catch (StorageClientException) { }
        }
    }

    public class when_retrieving_non_existing_blob : given_blob_storage_with_existing_root_container
    {
        private readonly byte[] bytes;

        public when_retrieving_non_existing_blob()
        {
            bytes = sut.Find(Guid.NewGuid().ToString());
        }

        [Fact]
        public void then_returns_null()
        {
            Assert.Null(bytes);
        }

        [Fact]
        public void then_can_delete_blob()
        {
            sut.Delete(Guid.NewGuid().ToString());
        }
    }

    public class when_saving_blob : given_blob_storage
    {
        private readonly byte[] bytes;

        private readonly string id;

        public when_saving_blob()
        {
            id = Guid.NewGuid().ToString();
            bytes = Guid.NewGuid().ToByteArray();

            sut.Save(id, "text/plain", bytes);
        }

        [Fact]
        public void then_writes_blob()
        {
            var client = account.CreateCloudBlobClient();
            var blobReference = client.GetBlobReference(rootContainerName + '/' + id);

            blobReference.FetchAttributes();
        }

        [Fact]
        public void then_can_find_blob()
        {
            var retrievedBytes = sut.Find(id);

            Assert.Equal(bytes, retrievedBytes);
        }

        [Fact]
        public void then_can_delete_blob()
        {
            sut.Delete(id);

            var retrievedBytes = sut.Find(id);

            Assert.Null(retrievedBytes);
        }

        [Fact]
        public void then_can_delete_multiple_times()
        {
            sut.Delete(id);
            sut.Delete(id);

            var retrievedBytes = sut.Find(id);

            Assert.Null(retrievedBytes);
        }

        [Fact]
        public void then_can_overwrite_blob()
        {
            var newBytes = Encoding.UTF8.GetBytes(Guid.NewGuid() + Guid.NewGuid().ToString());

            sut.Save(id, "text/plain", newBytes);

            var retrievedBytes = sut.Find(id);

            Assert.Equal(newBytes, retrievedBytes);
        }
    }

    public class when_saving_blob_with_compound_id : given_blob_storage
    {
        private readonly byte[] bytes;

        private readonly string id;

        public when_saving_blob_with_compound_id()
        {
            id = Guid.NewGuid().ToString() + '/' + Guid.NewGuid();
            bytes = Guid.NewGuid().ToByteArray();

            sut.Save(id, "text/plain", bytes);
        }

        [Fact]
        public void then_writes_blob()
        {
            var client = account.CreateCloudBlobClient();
            var blobReference = client.GetBlobReference(rootContainerName + '/' + id);

            blobReference.FetchAttributes();
        }

        [Fact]
        public void then_can_find_blob()
        {
            var retrievedBytes = sut.Find(id);

            Assert.Equal(bytes, retrievedBytes);
        }
    }
}