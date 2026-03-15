using NetworkMonitor.Objects;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects;

public class SearchIndexRequestTests
{
    [Fact]
    public void QueryIndexRequest_DefaultsMemoryFields()
    {
        var request = new QueryIndexRequest();

        Assert.Equal(string.Empty, request.UserId);
        Assert.Equal(string.Empty, request.SessionId);
        Assert.Equal(0, request.TopK);
        Assert.False(request.IncludeToolTurns);
        Assert.False(request.IncludeMetadata);
        Assert.Equal(string.Empty, request.AnchorDocId);
        Assert.Equal(string.Empty, request.AnchorChunkId);
        Assert.Equal(0, request.NeighborWindow);
        Assert.Equal(string.Empty, request.FilterDocId);
        Assert.Equal(string.Empty, request.FilterChunkId);
        Assert.Equal(string.Empty, request.FilterSourceFile);
        Assert.Equal(0, request.FilterPageStart);
        Assert.Equal(0, request.FilterPageEnd);
        Assert.Equal(0, request.FilterChunkIndexMin);
        Assert.Equal(0, request.FilterChunkIndexMax);
        Assert.Equal(VectorSearchMode.content, request.VectorSearchMode);
    }

    [Fact]
    public void QueryIndexRequest_SetVectorSearchModeFromString_ParsesValidAndFallbacks()
    {
        var request = new QueryIndexRequest();

        request.SetVectorSearchModeFromString("summary");
        Assert.Equal(VectorSearchMode.summary, request.VectorSearchMode);

        request.SetVectorSearchModeFromString("not-a-mode");
        Assert.Equal(VectorSearchMode.content, request.VectorSearchMode);
    }
}
