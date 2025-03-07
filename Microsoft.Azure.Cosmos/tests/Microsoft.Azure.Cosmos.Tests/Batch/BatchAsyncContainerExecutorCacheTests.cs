﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]    
    public class BatchAsyncContainerExecutorCacheTests
    {
        [TestMethod]
        public async Task ConcurrentGet_ReturnsSameExecutorInstance()
        {
            CosmosClientContext context = this.MockClientContext();

            DatabaseCore db = new DatabaseCore(context, "test");

            List<Task<ContainerCore>> tasks = new List<Task<ContainerCore>>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Run(() => Task.FromResult(new ContainerCore(context, db, "test"))));
            }

            await Task.WhenAll(tasks);

            BatchAsyncContainerExecutor firstExecutor = tasks[0].Result.BatchExecutor;
            Assert.IsNotNull(firstExecutor);
            for (int i = 1; i < 20; i++)
            {
                BatchAsyncContainerExecutor otherExecutor = tasks[i].Result.BatchExecutor;
                Assert.AreEqual(firstExecutor, otherExecutor);
            }
        }

        [TestMethod]
        [Timeout(60000)]
        public async Task SingleTaskScheduler_ExecutorTest()
        {
            CosmosClientContext context = this.MockClientContext();

            DatabaseCore db = new DatabaseCore(context, "test");

            List<Task<ContainerCore>> tasks = new List<Task<ContainerCore>>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(
                    Task.Factory.StartNew(() => new ContainerCore(context, db, "test"),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    new SingleTaskScheduler()));
            }

            await Task.WhenAll(tasks);

            BatchAsyncContainerExecutor firstExecutor = tasks[0].Result.BatchExecutor;
            Assert.IsNotNull(firstExecutor);
            for (int i = 1; i < 20; i++)
            {
                BatchAsyncContainerExecutor otherExecutor = tasks[i].Result.BatchExecutor;
                Assert.AreEqual(firstExecutor, otherExecutor);
            }
        }

        [TestMethod]
        public void Null_When_OptionsOff()
        {
            CosmosClientContext context = this.MockClientContext(allowBulkExecution: false);

            DatabaseCore db = new DatabaseCore(context, "test");
            ContainerCore container = new ContainerCore(context, db, "test");
            Assert.IsNull(container.BatchExecutor);
        }

        private CosmosClientContext MockClientContext(bool allowBulkExecution = true)
        {
            Mock<CosmosClient> mockClient = new Mock<CosmosClient>();
            mockClient.Setup(x => x.Endpoint).Returns(new Uri("http://localhost"));

            return ClientContextCore.Create(
                mockClient.Object,
                new MockDocumentClient(),
                new CosmosClientOptions() { AllowBulkExecution = allowBulkExecution });
        }
    }
}
