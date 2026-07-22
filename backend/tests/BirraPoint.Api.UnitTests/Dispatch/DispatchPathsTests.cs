using BirraPoint.Api.Features.Dispatch;

namespace BirraPoint.Api.UnitTests.Dispatch;

public sealed class DispatchPathsTests
{
    [Fact]
    public void ZipEntryPath_builds_the_FR_040_hierarchy()
    {
        var participantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var path = DispatchPaths.ZipEntryPath("Copa BirraPoint 2026", participantId, "21A", "B7K2");

        Assert.Equal($"Copa BirraPoint 2026/{participantId}/21A_B7K2.pdf", path);
    }

    [Fact]
    public void ZipEntryPath_does_not_throw_or_strip_characters_that_would_be_awkward_in_a_real_filesystem_path()
    {
        var participantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // ZIP entry names have no reserved characters the way real filesystem paths do — '/' just
        // nests an extra folder inside the participant's own folder, which every ZIP reader
        // (including System.IO.Compression.ZipArchive) handles without complaint.
        var path = DispatchPaths.ZipEntryPath("Copa: Norte/Sur 2026", participantId, "21A", "B7K2");

        Assert.Equal($"Copa: Norte/Sur 2026/{participantId}/21A_B7K2.pdf", path);
    }
}
