﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyLab.Mq;
using Newtonsoft.Json;
using Tests.Common;
using TestServer;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests
{
    public class ConsumingBehavior : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _appFactory;
        private readonly ITestOutputHelper _output;

        public ConsumingBehavior(WebApplicationFactory<Startup> appFactory, ITestOutputHelper output)
        {
            _appFactory = appFactory;
            _output = output;
        }

        [Fact]
        public async Task ShouldConsumeSimpleMessage()
        {
            //Arrange
            var queueId = Guid.NewGuid().ToString("N");
            var client = _appFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddMqConsuming(registrar =>
                    {
                        registrar.RegisterConsumer(TestSimpleMqConsumer<TestSimpleMqLogic>.Create(queueId));
                    })
                        .AddSingleton<IMqConnectionProvider, TestConnectionProvider>();
                });
            }).CreateClient();

            using var queueCtx = TestQueue.CreateWithId(queueId);
            var sender = queueCtx.CreateSender();

            //Act
            sender.Queue(new TestMqMsg { Content = "foo" });
            await Task.Delay(500);

            var resp = await client.GetAsync("test/single");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<SingleMessageTestBox>(respStr);

            //Assert
            Assert.Equal("foo", testBox.AckMsg.Payload.Content);
            Assert.Null(testBox.RejectedMsg);

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldRejectSimpleMessage()
        {
            //Arrange
            var queueId = Guid.NewGuid().ToString("N");
            var client = _appFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddMqConsuming(registrar =>
                        {
                            registrar.RegisterConsumer(TestSimpleMqConsumer<TestSimpleMqLogicWithReject>.Create(queueId));
                        })
                        .AddSingleton<IMqConnectionProvider, TestConnectionProvider>();
                });
            }).CreateClient();

            using var queueCtx = TestQueue.CreateWithId(queueId);
            var sender = queueCtx.CreateSender();

            //Act
            sender.Queue(new TestMqMsg { Content = "foo" });
            await Task.Delay(500);

            var resp = await client.GetAsync("test/single-with-reject");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<SingleMessageTestBox>(respStr);

            //Assert
            Assert.NotNull(testBox.AckMsg);
            Assert.Equal("foo", testBox.AckMsg.Payload.Content);
            Assert.NotNull(testBox.RejectedMsg);
            Assert.Equal("foo", testBox.RejectedMsg.Payload.Content);

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldConsumeMessageBatch()
        {
            //Arrange
            var queueId = Guid.NewGuid().ToString("N");
            var client = _appFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddMqConsuming(registrar =>
                    {
                        registrar.RegisterConsumer(TestBatchMqConsumer<TestBatchMqLogic>.Create(queueId));
                    })
                        .AddSingleton<IMqConnectionProvider, TestConnectionProvider>();
                });
            }).CreateClient();

            using var queueCtx = TestQueue.CreateWithId(queueId);
            var sender = queueCtx.CreateSender();

            //Act
            sender.Queue(new TestMqMsg { Content = "foo" });
            sender.Queue(new TestMqMsg { Content = "bar" });
            await Task.Delay(500);

            var resp = await client.GetAsync("test/batch");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<BatchMessageTestBox>(respStr);

            //Assert
            Assert.Null(testBox.RejectedMsgs);
            Assert.NotNull(testBox.AckMsgs);
            Assert.Equal(2, testBox.AckMsgs.Length);
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "foo");
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "bar");

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldRejectMessageBatch()
        {
            //Arrange
            var queueId = Guid.NewGuid().ToString("N");
            var client = _appFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddMqConsuming(registrar =>
                    {
                        registrar.RegisterConsumer(TestBatchMqConsumer<TestBatchMqLogicWithReject>.Create(queueId));
                    })
                        .AddSingleton<IMqConnectionProvider, TestConnectionProvider>();
                });
            }).CreateClient();

            using var queueCtx = TestQueue.CreateWithId(queueId);
            var sender = queueCtx.CreateSender();

            //Act
            sender.Queue(new TestMqMsg { Content = "foo" });
            sender.Queue(new TestMqMsg { Content = "bar" });
            await Task.Delay(500);

            var resp = await client.GetAsync("test/batch-with-reject");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            var testBox = JsonConvert.DeserializeObject<BatchMessageTestBox>(respStr);

            //Assert
            Assert.NotNull(testBox.AckMsgs);
            Assert.Equal(2, testBox.AckMsgs.Length);
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "foo");
            Assert.Contains(testBox.AckMsgs, m => m.Payload.Content == "bar");
            Assert.NotNull(testBox.RejectedMsgs);
            Assert.Equal(2, testBox.RejectedMsgs.Length);
            Assert.Contains(testBox.RejectedMsgs, m => m.Payload.Content == "foo");
            Assert.Contains(testBox.RejectedMsgs, m => m.Payload.Content == "bar");

            await PrintStatus(client);
        }

        [Fact]
        public async Task ShouldUseIncomingMessageScopeServices()
        {
            var queueId = Guid.NewGuid().ToString("N");
            var client = _appFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddMqConsuming(registrar =>
                        {
                            registrar.RegisterConsumer(TestSimpleMqConsumer<MqLogicWithScopedDependency>.Create(queueId));
                        })
                        .AddSingleton<IMqConnectionProvider, TestConnectionProvider>();
                });
            }).CreateClient();

            using var queueCtx = TestQueue.CreateWithId(queueId);
            var sender = queueCtx.CreateSender();

            //Act
            sender.Queue(new TestMqMsg { Content = "foo" });
            await Task.Delay(500);

            var resp = await client.GetAsync("test/from-scope");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine(respStr);
            resp.EnsureSuccessStatusCode();

            //Assert
            Assert.Equal("foo", respStr);

            await PrintStatus(client);
        }

        async Task PrintStatus(HttpClient client)
        {
            var resp = await client.GetAsync("status");
            var respStr = await resp.Content.ReadAsStringAsync();

            _output.WriteLine("");
            if (!resp.IsSuccessStatusCode)
            {
                _output.WriteLine("Get status error: " + resp.StatusCode);
            }
            else
            {
                _output.WriteLine("STATUS: ");
            }

            _output.WriteLine(respStr);
        }
    }
}
