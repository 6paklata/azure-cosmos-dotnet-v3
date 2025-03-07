﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using Microsoft.Azure.Cosmos.Fluent;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class CosmosClientOptionsUnitTests
    {
        public const string AccountEndpoint = "https://localhost:8081/";
        public const string ConnectionString = "AccountEndpoint=https://localtestcosmos.documents.azure.com:443/;AccountKey=425Mcv8CXQqzRNCgFNjIhT424GK99CKJvASowTnq15Vt8LeahXTcN5wt3342vQ==;";

        [TestMethod]
        public void VerifyCosmosConfigurationPropertiesGetUpdated()
        {
            string endpoint = AccountEndpoint;
            string key = Guid.NewGuid().ToString();
            string region = Regions.WestCentralUS;
            ConnectionMode connectionMode = ConnectionMode.Gateway;
            TimeSpan requestTimeout = TimeSpan.FromDays(1);
            int maxConnections = 9001;
            string userAgentSuffix = "testSuffix";
            RequestHandler preProcessHandler = new TestHandler();
            ApiType apiType = ApiType.Sql;
            int maxRetryAttemptsOnThrottledRequests = 9999;
            TimeSpan maxRetryWaitTime = TimeSpan.FromHours(6);
            bool enableTcpConnectionEndpointRediscovery = true;
            CosmosSerializationOptions cosmosSerializerOptions = new CosmosSerializationOptions()
            {
                IgnoreNullValues = true,
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            };
            TimeSpan idleTcpConnectionTimeout = new TimeSpan(0, 10, 0);
            TimeSpan openTcpConnectionTimeout = new TimeSpan(0, 0, 5);
            int maxRequestsPerTcpConnection = 30;
            int maxTcpConnectionsPerEndpoint = 65535;
            Cosmos.PortReuseMode portReuseMode = Cosmos.PortReuseMode.PrivatePortPool;
            IWebProxy webProxy = new TestWebProxy();

            CosmosClientBuilder cosmosClientBuilder = new CosmosClientBuilder(
                accountEndpoint: endpoint,
                authKeyOrResourceToken: key);

            CosmosClient cosmosClient = cosmosClientBuilder.Build(new MockDocumentClient());
            CosmosClientOptions clientOptions = cosmosClient.ClientOptions;

            Assert.AreEqual(endpoint, cosmosClient.Endpoint.OriginalString, "AccountEndpoint did not save correctly");
            Assert.AreEqual(key, cosmosClient.AccountKey, "AccountKey did not save correctly");

            //Verify the default values are different from the new values
            Assert.AreNotEqual(region, clientOptions.ApplicationRegion);
            Assert.IsNull(clientOptions.ApplicationPreferredRegions);
            Assert.AreNotEqual(connectionMode, clientOptions.ConnectionMode);
            Assert.AreNotEqual(maxConnections, clientOptions.GatewayModeMaxConnectionLimit);
            Assert.AreNotEqual(requestTimeout, clientOptions.RequestTimeout);
            Assert.AreNotEqual(userAgentSuffix, clientOptions.ApplicationName);
            Assert.AreNotEqual(apiType, clientOptions.ApiType);
            Assert.IsFalse(clientOptions.AllowBulkExecution);
            Assert.AreEqual(0, clientOptions.CustomHandlers.Count);
            Assert.IsNull(clientOptions.SerializerOptions);
            Assert.IsNull(clientOptions.Serializer);
            Assert.IsNull(clientOptions.WebProxy);
            Assert.IsFalse(clientOptions.LimitToEndpoint);
            Assert.IsFalse(clientOptions.EnableTcpConnectionEndpointRediscovery);

            //Verify GetConnectionPolicy returns the correct values for default
            ConnectionPolicy policy = clientOptions.GetConnectionPolicy();
            Assert.AreEqual(ConnectionMode.Direct, policy.ConnectionMode);
            Assert.AreEqual(Protocol.Tcp, policy.ConnectionProtocol);
            Assert.AreEqual(clientOptions.GatewayModeMaxConnectionLimit, policy.MaxConnectionLimit);
            Assert.AreEqual(clientOptions.RequestTimeout, policy.RequestTimeout);
            Assert.IsNull(policy.IdleTcpConnectionTimeout);
            Assert.IsNull(policy.OpenTcpConnectionTimeout);
            Assert.IsNull(policy.MaxRequestsPerTcpConnection);
            Assert.IsNull(policy.MaxTcpConnectionsPerEndpoint);
            Assert.IsTrue(policy.EnableEndpointDiscovery);
            Assert.IsFalse(policy.EnableTcpConnectionEndpointRediscovery);

            cosmosClientBuilder.WithApplicationRegion(region)
                .WithConnectionModeGateway(maxConnections, webProxy)
                .WithRequestTimeout(requestTimeout)
                .WithApplicationName(userAgentSuffix)
                .AddCustomHandlers(preProcessHandler)
                .WithApiType(apiType)
                .WithThrottlingRetryOptions(maxRetryWaitTime, maxRetryAttemptsOnThrottledRequests)
                .WithBulkExecution(true)
                .WithSerializerOptions(cosmosSerializerOptions);

            cosmosClient = cosmosClientBuilder.Build(new MockDocumentClient());
            clientOptions = cosmosClient.ClientOptions;

            //Verify all the values are updated
            Assert.AreEqual(region, clientOptions.ApplicationRegion);
            Assert.IsNull(clientOptions.ApplicationPreferredRegions);
            Assert.AreEqual(connectionMode, clientOptions.ConnectionMode);
            Assert.AreEqual(maxConnections, clientOptions.GatewayModeMaxConnectionLimit);
            Assert.AreEqual(requestTimeout, clientOptions.RequestTimeout);
            Assert.AreEqual(userAgentSuffix, clientOptions.ApplicationName);
            Assert.AreEqual(preProcessHandler, clientOptions.CustomHandlers[0]);
            Assert.AreEqual(apiType, clientOptions.ApiType);
            Assert.AreEqual(maxRetryAttemptsOnThrottledRequests, clientOptions.MaxRetryAttemptsOnRateLimitedRequests);
            Assert.AreEqual(maxRetryWaitTime, clientOptions.MaxRetryWaitTimeOnRateLimitedRequests);
            Assert.AreEqual(cosmosSerializerOptions.IgnoreNullValues, clientOptions.SerializerOptions.IgnoreNullValues);
            Assert.AreEqual(cosmosSerializerOptions.PropertyNamingPolicy, clientOptions.SerializerOptions.PropertyNamingPolicy);
            Assert.AreEqual(cosmosSerializerOptions.Indented, clientOptions.SerializerOptions.Indented);
            Assert.IsTrue(object.ReferenceEquals(webProxy, clientOptions.WebProxy));
            Assert.IsTrue(clientOptions.AllowBulkExecution);

            //Verify GetConnectionPolicy returns the correct values
            policy = clientOptions.GetConnectionPolicy();
            Assert.AreEqual(region, policy.PreferredLocations[0]);
            Assert.AreEqual(ConnectionMode.Gateway, policy.ConnectionMode);
            Assert.AreEqual(Protocol.Https, policy.ConnectionProtocol);
            Assert.AreEqual(maxConnections, policy.MaxConnectionLimit);
            Assert.AreEqual(requestTimeout, policy.RequestTimeout);
            Assert.IsTrue(policy.UserAgentSuffix.Contains(userAgentSuffix));
            Assert.IsTrue(policy.UseMultipleWriteLocations);
            Assert.AreEqual(maxRetryAttemptsOnThrottledRequests, policy.RetryOptions.MaxRetryAttemptsOnThrottledRequests);
            Assert.AreEqual((int)maxRetryWaitTime.TotalSeconds, policy.RetryOptions.MaxRetryWaitTimeInSeconds);

            IReadOnlyList<string> preferredLocations = new List<string>() { Regions.AustraliaCentral, Regions.AustraliaCentral2 };
            //Verify Direct Mode settings
            cosmosClientBuilder = new CosmosClientBuilder(
                accountEndpoint: endpoint,
                authKeyOrResourceToken: key);
            cosmosClientBuilder.WithConnectionModeDirect(
                idleTcpConnectionTimeout,
                openTcpConnectionTimeout,
                maxRequestsPerTcpConnection,
                maxTcpConnectionsPerEndpoint,
                portReuseMode,
                enableTcpConnectionEndpointRediscovery);

            cosmosClient = cosmosClientBuilder.Build(new MockDocumentClient());
            clientOptions = cosmosClient.ClientOptions;
            clientOptions.ApplicationPreferredRegions = preferredLocations;
            //Verify all the values are updated
            Assert.AreEqual(idleTcpConnectionTimeout, clientOptions.IdleTcpConnectionTimeout);
            Assert.AreEqual(openTcpConnectionTimeout, clientOptions.OpenTcpConnectionTimeout);
            Assert.AreEqual(maxRequestsPerTcpConnection, clientOptions.MaxRequestsPerTcpConnection);
            Assert.AreEqual(maxTcpConnectionsPerEndpoint, clientOptions.MaxTcpConnectionsPerEndpoint);
            Assert.AreEqual(portReuseMode, clientOptions.PortReuseMode);
            Assert.IsTrue(clientOptions.EnableTcpConnectionEndpointRediscovery);
            CollectionAssert.AreEqual(preferredLocations.ToArray(), clientOptions.ApplicationPreferredRegions.ToArray());

            //Verify GetConnectionPolicy returns the correct values
            policy = clientOptions.GetConnectionPolicy();
            Assert.AreEqual(idleTcpConnectionTimeout, policy.IdleTcpConnectionTimeout);
            Assert.AreEqual(openTcpConnectionTimeout, policy.OpenTcpConnectionTimeout);
            Assert.AreEqual(maxRequestsPerTcpConnection, policy.MaxRequestsPerTcpConnection);
            Assert.AreEqual(maxTcpConnectionsPerEndpoint, policy.MaxTcpConnectionsPerEndpoint);
            Assert.AreEqual(portReuseMode, policy.PortReuseMode);
            Assert.IsTrue(policy.EnableTcpConnectionEndpointRediscovery);
            CollectionAssert.AreEqual(preferredLocations.ToArray(), policy.PreferredLocations.ToArray());
        }

        [TestMethod]
        public void VerifyPortReuseModeIsSyncedWithDirect()
        {
            CollectionAssert.AreEqual(
                Enum.GetNames(typeof(PortReuseMode)).OrderBy(x => x).ToArray(),
                Enum.GetNames(typeof(Cosmos.PortReuseMode)).OrderBy(x => x).ToArray()
            );

            CollectionAssert.AreEqual(
                Enum.GetValues(typeof(PortReuseMode)).Cast<int>().ToArray(),
                Enum.GetValues(typeof(Cosmos.PortReuseMode)).Cast<int>().ToArray()
            );
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ThrowOnBadDelegatingHandler()
        {
            RequestHandler handler = new TestHandler();
            RequestHandler innerHandler = new TestHandler();

            //Inner handler is required to be null to allow the client to connect it to other handlers
            handler.InnerHandler = innerHandler;
            new CosmosClientBuilder(CosmosClientOptionsUnitTests.AccountEndpoint, "testKey")
                .AddCustomHandlers(handler);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void ThrowOnNullEndpoint()
        {
            new CosmosClientBuilder(null, "testKey");
        }

        [TestMethod]
        public void UserAgentContainsEnvironmentInformation()
        {
            EnvironmentInformation environmentInformation = new EnvironmentInformation();
            string expectedValue = "cosmos-netstandard-sdk/" + environmentInformation.ClientVersion;
            CosmosClientOptions cosmosClientOptions = new CosmosClientOptions();
            string userAgentSuffix = "testSuffix";
            cosmosClientOptions.ApplicationName = userAgentSuffix;
            Assert.AreEqual(userAgentSuffix, cosmosClientOptions.ApplicationName);
            UserAgentContainer userAgentContainer = cosmosClientOptions.BuildUserAgentContainer();
            Assert.AreEqual(userAgentSuffix, userAgentContainer.Suffix);
            Assert.IsTrue(userAgentContainer.UserAgent.StartsWith(expectedValue));
            Assert.IsTrue(userAgentContainer.UserAgent.EndsWith(userAgentSuffix));

            ConnectionPolicy connectionPolicy = cosmosClientOptions.GetConnectionPolicy();
            Assert.AreEqual(userAgentSuffix, connectionPolicy.UserAgentSuffix);
            Assert.IsTrue(connectionPolicy.UserAgentContainer.UserAgent.StartsWith(expectedValue));
            Assert.IsTrue(connectionPolicy.UserAgentContainer.UserAgent.EndsWith(userAgentSuffix));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ThrowOnSerializerOptionsWithCustomSerializer()
        {
            CosmosClientOptions options = new CosmosClientOptions()
            {
                Serializer = new CosmosJsonDotNetSerializer()
            };

            options.SerializerOptions = new CosmosSerializationOptions();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ThrowOnCustomSerializerWithSerializerOptions()
        {
            CosmosClientOptions options = new CosmosClientOptions()
            {
                SerializerOptions = new CosmosSerializationOptions()
            };

            options.Serializer = new CosmosJsonDotNetSerializer();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void ThrowOnNullConnectionString()
        {
            new CosmosClientBuilder(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ThrowOnMissingAccountKeyInConnectionString()
        {
            string invalidConnectionString = "AccountEndpoint=https://localtestcosmos.documents.azure.com:443/;";
            new CosmosClientBuilder(invalidConnectionString);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ThrowOnMissingAccountEndpointInConnectionString()
        {
            string invalidConnectionString = "AccountKey=425Mcv8CXQqzRNCgFNjIhT424GK99CKJvASowTnq15Vt8LeahXTcN5wt3342vQ==;";
            new CosmosClientBuilder(invalidConnectionString);
        }

        [TestMethod]
        public void VerifyGetConnectionPolicyThrowIfDirectTcpSettingAreUsedInGatewayMode()
        {
            TimeSpan idleTcpConnectionTimeout = new TimeSpan(0, 10, 0);
            TimeSpan openTcpConnectionTimeout = new TimeSpan(0, 0, 5);
            int maxRequestsPerTcpConnection = 30;
            int maxTcpConnectionsPerEndpoint = 65535;

            CosmosClientOptions cosmosClientOptions = new CosmosClientOptions()
            {
                ConnectionMode = ConnectionMode.Gateway
            };
            
            Assert.ThrowsException<ArgumentException>(() => { cosmosClientOptions.IdleTcpConnectionTimeout = idleTcpConnectionTimeout; });
            Assert.ThrowsException<ArgumentException>(() => { cosmosClientOptions.OpenTcpConnectionTimeout = openTcpConnectionTimeout; });
            Assert.ThrowsException<ArgumentException>(() => { cosmosClientOptions.MaxRequestsPerTcpConnection = maxRequestsPerTcpConnection; });
            Assert.ThrowsException<ArgumentException>(() => { cosmosClientOptions.MaxTcpConnectionsPerEndpoint = maxTcpConnectionsPerEndpoint; });
        }

        [TestMethod]
        public void VerifyHttpClientHandlerSettingsThrowIfNotUsedInGatewayMode()
        {
            CosmosClientOptions cosmosClientOptions = new CosmosClientOptions()
            {
                ConnectionMode = ConnectionMode.Direct
            };

            Assert.ThrowsException<ArgumentException>(() => { cosmosClientOptions.WebProxy = new TestWebProxy(); });
        }

        [TestMethod]
        public void VerifyHttpClientHandlerIsSet()
        {
            string endpoint = AccountEndpoint;
            string key = "425Mcv8CXQqzRNCgFNjIhT424GK99CKJvASowTnq15Vt8LeahXTcN5wt3342vQ==";

            IWebProxy webProxy = new TestWebProxy();

            CosmosClientBuilder cosmosClientBuilder = new CosmosClientBuilder(
                accountEndpoint: endpoint,
                authKeyOrResourceToken: key);
            cosmosClientBuilder.WithConnectionModeGateway(
                maxConnectionLimit: null,
                webProxy: webProxy);

            CosmosClient cosmosClient = cosmosClientBuilder.Build();
            DelegatingHandler handler = (DelegatingHandler) cosmosClient.DocumentClient.httpMessageHandler;
            HttpClientHandler innerHandler = (HttpClientHandler)handler.InnerHandler;

            Assert.IsTrue(object.ReferenceEquals(webProxy, innerHandler.Proxy));
        }

        [TestMethod]
        public void VerifyCorrectProtocolIsSet()
        {
            CosmosClientOptions cosmosClientOptions = new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway };
            Assert.AreEqual(Protocol.Https, cosmosClientOptions.ConnectionProtocol);

            cosmosClientOptions = new CosmosClientOptions { ConnectionMode = ConnectionMode.Direct };
            Assert.AreEqual(Protocol.Tcp, cosmosClientOptions.ConnectionProtocol);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void VerifyLimitToEndpointSettings()
        {
            CosmosClientOptions cosmosClientOptions = new CosmosClientOptions { ApplicationRegion = Regions.EastUS, LimitToEndpoint = true };
            cosmosClientOptions.GetConnectionPolicy();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void VerifyLimitToEndpointSettingsWithPreferredRegions()
        {
            CosmosClientOptions cosmosClientOptions = new CosmosClientOptions { ApplicationPreferredRegions = new List<string>() { Regions.EastUS }, LimitToEndpoint = true };
            cosmosClientOptions.GetConnectionPolicy();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void VerifyApplicationRegionSettingsWithPreferredRegions()
        {
            CosmosClientOptions cosmosClientOptions = new CosmosClientOptions { ApplicationPreferredRegions = new List<string>() { Regions.EastUS }, ApplicationRegion = Regions.EastUS };
            cosmosClientOptions.GetConnectionPolicy();
        }

        [TestMethod]
        public void WithLimitToEndpointAffectsEndpointDiscovery()
        {
            CosmosClientBuilder cosmosClientBuilder = new CosmosClientBuilder(
                accountEndpoint: AccountEndpoint,
                authKeyOrResourceToken: Guid.NewGuid().ToString());

            CosmosClientOptions cosmosClientOptions = cosmosClientBuilder.Build(new MockDocumentClient()).ClientOptions;
            Assert.IsFalse(cosmosClientOptions.LimitToEndpoint);

            ConnectionPolicy connectionPolicy = cosmosClientOptions.GetConnectionPolicy();
            Assert.IsTrue(connectionPolicy.EnableEndpointDiscovery);

            cosmosClientBuilder
                .WithLimitToEndpoint(true);

            cosmosClientOptions = cosmosClientBuilder.Build(new MockDocumentClient()).ClientOptions;
            Assert.IsTrue(cosmosClientOptions.LimitToEndpoint);

            connectionPolicy = cosmosClientOptions.GetConnectionPolicy();
            Assert.IsFalse(connectionPolicy.EnableEndpointDiscovery);
        }

        private class TestWebProxy : IWebProxy
        {
            public ICredentials Credentials { get; set; }

            public Uri GetProxy(Uri destination)
            {
                return new Uri("https://www.test.com");
            }

            public bool IsBypassed(Uri host)
            {
                return false;
            }
        }
    }
}
