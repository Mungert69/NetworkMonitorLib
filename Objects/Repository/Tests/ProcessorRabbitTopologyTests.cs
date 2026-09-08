using System;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetworkMonitor.Objects.Repository.Helpers;
using RabbitMQ.Client;
using Xunit;

namespace NetworkMonitor.Objects.Repository.Tests;

public class ProcessorRabbitTopologyTests
{
    [Fact]
    public void BuildsAndParsesV2ResourceNames()
    {
        const string routingId = "user_123-agent-7";
        const string operation = "processorCommand";

        string routingKey = ProcessorRabbitTopology.BuildRoutingKey(routingId, operation);
        string queueName = ProcessorRabbitTopology.BuildQueueName(routingId, operation);

        Assert.Equal("user_123-agent-7.processorCommand", routingKey);
        Assert.Equal(
            "monitorProcessor.queue.user_123-agent-7.processorCommand",
            queueName);
        Assert.True(ProcessorRabbitTopology.TryParseRoutingKey(
            routingKey,
            out string parsedRoutingId,
            out string parsedOperation));
        Assert.Equal(routingId, parsedRoutingId);
        Assert.Equal(operation, parsedOperation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains.dot")]
    [InlineData("contains space")]
    [InlineData("contains/slash")]
    public void RejectsInvalidRoutingIds(string routingId)
    {
        Assert.False(ProcessorRabbitTopology.IsValidRoutingId(routingId));
        Assert.Throws<ArgumentException>(() =>
            ProcessorRabbitTopology.BuildRoutingKey(routingId, "processorCommand"));
    }

    [Fact]
    public void RejectsUnknownOperation()
    {
        Assert.False(ProcessorRabbitTopology.IsSupportedOperation("arbitraryOperation"));
        Assert.Throws<ArgumentException>(() =>
            ProcessorRabbitTopology.BuildRoutingKey("user-agent", "arbitraryOperation"));
    }

    [Fact]
    public void RabbitRepoUsesTopicForV2CommandsExchange()
    {
        var repo = new RabbitRepo(
            NullLogger<RabbitRepo>.Instance,
            new SystemUrl());

        Assert.Equal(
            ExchangeType.Topic,
            repo.GetExchangeType(ProcessorRabbitTopology.CommandsExchange));
        Assert.Equal(ExchangeType.Fanout, repo.GetExchangeType("legacyExchange"));
    }

    [Fact]
    public void TopologyVersionDefaultsToLegacyAndSurvivesCopies()
    {
        var legacy = new ProcessorObj();
        var versionTwo = new ProcessorObj
        {
            RabbitTopologyVersion = ProcessorRabbitTopology.Version
        };

        Assert.Equal(ProcessorRabbitTopology.LegacyVersion, legacy.RabbitTopologyVersion);
        Assert.Equal(
            ProcessorRabbitTopology.Version,
            new ProcessorObj(versionTwo, showAuthKey: false).RabbitTopologyVersion);
        Assert.Equal(
            ProcessorRabbitTopology.LegacyVersion,
            ProcessorRabbitTopology.NormalizeVersion(0));
        Assert.Equal(
            ProcessorRabbitTopology.LegacyVersion,
            ProcessorRabbitTopology.NormalizeVersion(999));
    }

    [Fact]
    public async Task V2PublisherUsesSharedExchangeAndProcessorRoute()
    {
        var rabbitRepo = new Mock<IRabbitRepo>();
        var message = new ProcessorObj { AppID = "user_123-agent-7" };

        await ProcessorRabbitPublisher.PublishAsync(
            rabbitRepo.Object,
            "user_123-agent-7",
            "processorCommand",
            message,
            ProcessorRabbitTopology.Version);

        rabbitRepo.Verify(repo => repo.PublishAsync(
            ProcessorRabbitTopology.CommandsExchange,
            message,
            "user_123-agent-7.processorCommand"), Times.Once);
        rabbitRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LegacyPublisherUsesPerProcessorExchange()
    {
        var rabbitRepo = new Mock<IRabbitRepo>();
        var message = new ProcessorObj { AppID = "agent-1" };

        await ProcessorRabbitPublisher.PublishAsync(
            rabbitRepo.Object,
            "agent-1",
            "processorCommand",
            message,
            ProcessorRabbitTopology.LegacyVersion);

        rabbitRepo.Verify(repo => repo.PublishAsync(
            "processorCommandagent-1",
            message,
            ""), Times.Once);
    }
}
