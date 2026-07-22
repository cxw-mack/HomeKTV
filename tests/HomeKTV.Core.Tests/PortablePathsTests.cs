using HomeKTV.Core.Portable;

namespace HomeKTV.Core.Tests;

public sealed class PortablePathsTests
{
    [Fact]
    public void ResolvesRelativeMediaPathUnderInjectedRoot()
    {
        var paths = new PortablePaths(Path.Combine(Path.GetTempPath(), "HomeKTV-D"));
        Assert.Equal(Path.Combine(paths.Root, "Media", "MV", "demo.mp4"), paths.Resolve("Media/MV/demo.mp4"));
    }

    [Fact]
    public void RejectsAbsoluteAndEscapingPaths()
    {
        var paths = new PortablePaths(Path.Combine(Path.GetTempPath(), "HomeKTV-root"));
        Assert.Throws<ArgumentException>(() => paths.Resolve(Path.GetFullPath("outside.mp4")));
        Assert.Throws<InvalidOperationException>(() => paths.Resolve("../outside.mp4"));
    }

    [Fact]
    public void StoredRelativePathSurvivesSimulatedDriveChange()
    {
        const string relative = "Media/MV/artist-song.mp4";
        var oldRoot = new PortablePaths(Path.Combine(Path.GetTempPath(), "Drive-D", "HomeKTV"));
        var newRoot = new PortablePaths(Path.Combine(Path.GetTempPath(), "Drive-F", "HomeKTV"));
        Assert.EndsWith(Path.Combine("HomeKTV", "Media", "MV", "artist-song.mp4"), oldRoot.Resolve(relative));
        Assert.EndsWith(Path.Combine("HomeKTV", "Media", "MV", "artist-song.mp4"), newRoot.Resolve(relative));
        Assert.NotEqual(oldRoot.Resolve(relative), newRoot.Resolve(relative));
    }
}

