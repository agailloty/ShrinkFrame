using System.Text.Json;
using ShrinkFrame.Application;
using ShrinkFrame.Infrastructure.Immich;

namespace ShrinkFrame.Infrastructure.Tests;

[TestClass]
public sealed class ImmichVideoBrowserTests
{
    [DataRow("{\"nextPage\":2}", 2)]
    [DataRow("{\"nextPage\":\"3\"}", 3)]
    [DataRow("{\"nextPage\":null}", null)]
    [TestMethod]
    public void NextPageAcceptsImmichNumberAndStringRepresentations(string json, int? expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(expected, ImmichVideoBrowser.ReadNullablePage(document.RootElement, "nextPage"));
    }

    [TestMethod]
    public void SizeSortIsGlobalAndPlacesUnknownSizesLast()
    {
        var items = new[] { Video("b", 10), Video("unknown", null), Video("a", 20) };

        CollectionAssert.AreEqual(new[] { "a", "b", "unknown" },
            ImmichVideoBrowser.SortBySize(items, descending: true).Select(x => x.AssetId).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "a", "unknown" },
            ImmichVideoBrowser.SortBySize(items, descending: false).Select(x => x.AssetId).ToArray());
    }

    private static ImmichVideoSummary Video(string id, long? size) => new(id, $"{id}.mp4", "video/mp4",
        DateTimeOffset.UnixEpoch, null, null, null, size);
}
