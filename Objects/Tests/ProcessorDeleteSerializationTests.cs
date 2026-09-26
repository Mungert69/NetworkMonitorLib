using System;
using System.Text.Json;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitor.Objects.Tests;

public sealed class ProcessorDeleteSerializationTests
{
    [Fact]
    public void EveryConcreteBackendSignedMessageHasGeneratedMetadata()
    {
        foreach (Type type in typeof(IBackendSignedMessage).Assembly.GetTypes()) {
            if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters ||
                !typeof(IBackendSignedMessage).IsAssignableFrom(type)) continue;
            Assert.True(SourceGenerationContext.Default.GetTypeInfo(type) != null,
                $"Missing JsonSerializable registration for signed message {type.FullName}");
        }
    }

    [Fact]
    public void DeleteRequest_RoundTripsThroughGeneratedMetadataAndSigningPayload()
    {
        var request = new ProcessorDeleteRequest {
            AppID = "user-device", RegistrationId = 42, RequestedBy = "owner",
            ExpiresAtUtc = new DateTime(2026, 9, 26, 18, 0, 0, DateTimeKind.Utc),
            BackendSignature = "test-signature"
        };
        byte[] payload = BackendMessageSignaturePayload.Create("processorDeleteRequest", "data", request);
        Assert.NotEmpty(payload);
        Assert.Equal("test-signature", request.BackendSignature);
        string json = JsonSerializer.Serialize(request, typeof(ProcessorDeleteRequest), SourceGenerationContext.Default);
        var received = Assert.IsType<ProcessorDeleteRequest>(JsonSerializer.Deserialize(json, typeof(ProcessorDeleteRequest), SourceGenerationContext.Default));
        Assert.Equal(request.AppID, received.AppID);
        Assert.Equal(request.RegistrationId, received.RegistrationId);
        Assert.Equal(request.RequestedBy, received.RequestedBy);
        Assert.Equal(request.ExpiresAtUtc, received.ExpiresAtUtc);
        Assert.Equal(request.BackendSignature, received.BackendSignature);
        Assert.Equal(payload, BackendMessageSignaturePayload.Create("processorDeleteRequest", "data", received));
        received.BackendSignature = "another-signature";
        Assert.Equal(payload, BackendMessageSignaturePayload.Create("processorDeleteRequest", "data", received));
        Assert.Equal("another-signature", received.BackendSignature);
        received.RegistrationId++;
        Assert.NotEqual(payload, BackendMessageSignaturePayload.Create("processorDeleteRequest", "data", received));
    }
}
