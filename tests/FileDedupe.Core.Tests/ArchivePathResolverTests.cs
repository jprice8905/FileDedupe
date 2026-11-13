using FileDedupe.Core.Archiving;

namespace FileDedupe.Core.Tests;

public class ArchivePathResolverTests
{
    private const string Root = @"D:\pictures";
    private const string Archive = @"E:\duplicates";

    [Fact]
    public void PreservesRelativePathUnderArchiveRoot()
    {
        var resolver = new ArchivePathResolver(_ => false);

        var result = resolver.Resolve(@"D:\pictures\familypictures\DSC-1015.jpg", Root, Archive);

        Assert.Equal(Path.GetFullPath(@"E:\duplicates\familypictures\DSC-1015.jpg"), result);
    }

    [Fact]
    public void ExistingArchiveFile_GetsThreeDigitSuffix()
    {
        var taken = Path.GetFullPath(@"E:\duplicates\familypictures\DSC-1015.jpg");
        var resolver = new ArchivePathResolver(p => string.Equals(p, taken, StringComparison.OrdinalIgnoreCase));

        var result = resolver.Resolve(@"D:\pictures\familypictures\DSC-1015.jpg", Root, Archive);

        Assert.Equal(Path.GetFullPath(@"E:\duplicates\familypictures\DSC-1015_001.jpg"), result);
    }

    [Fact]
    public void RepeatedCollisions_IncrementTheSuffix()
    {
        // Nothing on disk: the clashes come from earlier files in the same plan, which
        // is exactly the preview-mode case where no file has been written yet.
        var resolver = new ArchivePathResolver(_ => false);

        var first = resolver.Resolve(@"D:\pictures\a\DSC-1015.jpg", Root, Archive);
        var second = resolver.Resolve(@"D:\pictures\b\..\a\DSC-1015.jpg", Root, Archive);
        var third = resolver.Resolve(@"D:\pictures\a\DSC-1015.jpg", Root, Archive);

        Assert.Equal(Path.GetFullPath(@"E:\duplicates\a\DSC-1015.jpg"), first);
        Assert.Equal(Path.GetFullPath(@"E:\duplicates\a\DSC-1015_001.jpg"), second);
        Assert.Equal(Path.GetFullPath(@"E:\duplicates\a\DSC-1015_002.jpg"), third);
    }

    [Fact]
    public void SuffixSkipsNamesAlreadyPresentOnDisk()
    {
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(@"E:\duplicates\a\DSC-1015.jpg"),
            Path.GetFullPath(@"E:\duplicates\a\DSC-1015_001.jpg")
        };
        var resolver = new ArchivePathResolver(onDisk.Contains);

        var result = resolver.Resolve(@"D:\pictures\a\DSC-1015.jpg", Root, Archive);

        Assert.Equal(Path.GetFullPath(@"E:\duplicates\a\DSC-1015_002.jpg"), result);
    }

    [Fact]
    public void Reset_ReleasesPreviousReservations()
    {
        var resolver = new ArchivePathResolver(_ => false);
        var first = resolver.Resolve(@"D:\pictures\a\DSC-1015.jpg", Root, Archive);

        resolver.Reset();
        var afterReset = resolver.Resolve(@"D:\pictures\a\DSC-1015.jpg", Root, Archive);

        Assert.Equal(first, afterReset);
    }

    [Fact]
    public void FileOutsideItsRoot_FlattensToFileName_RatherThanEscapingTheArchive()
    {
        var resolver = new ArchivePathResolver(_ => false);

        var result = resolver.Resolve(@"C:\elsewhere\DSC-1015.jpg", Root, Archive);

        Assert.Equal(Path.GetFullPath(@"E:\duplicates\DSC-1015.jpg"), result);
    }

    [Fact]
    public void FileWithNoExtension_StillGetsSuffix()
    {
        var resolver = new ArchivePathResolver(_ => false);

        resolver.Resolve(@"D:\pictures\a\README", Root, Archive);
        var second = resolver.Resolve(@"D:\pictures\a\README", Root, Archive);

        Assert.Equal(Path.GetFullPath(@"E:\duplicates\a\README_001"), second);
    }
}
