using FileDedupe.Core.Archiving;
using FileDedupe.Core.Models;

namespace FileDedupe.Core.Tests;

/// <summary>
/// Covers the keeper-selection rules. These decide which copy survives, so a mistake
/// here archives the wrong file - the highest-consequence logic in the application.
/// </summary>
public class ArchivePlannerTests
{
    private const string ArchiveRoot = @"E:\duplicates";

    private static readonly RootPath Primary = new() { Id = 1, Path = @"D:\pictures", Ordinal = 0 };
    private static readonly RootPath Secondary = new() { Id = 2, Path = @"F:\backup", Ordinal = 1 };

    private static ArchivePlanner CreatePlanner() =>
        // Nothing exists in the archive, so collision handling stays out of these cases.
        new(new ArchivePathResolver(_ => false));

    private static FileRecord File(long id, long rootId, string fullPath, long length = 1024) => new()
    {
        Id = id,
        RootPathId = rootId,
        FullPath = fullPath,
        Name = Path.GetFileName(fullPath),
        Extension = Path.GetExtension(fullPath),
        DirectoryName = Path.GetDirectoryName(fullPath)!,
        Length = length
    };

    private static DuplicateGroup Group(params FileRecord[] files) =>
        new("0123456789abcdef0123456789abcdef", files[0].Length, files);

    [Fact]
    public void SingleCopyInPrimaryRoot_IsKept_AndOtherRootsAreArchived()
    {
        var keeper = File(1, Primary.Id, @"D:\pictures\family\DSC-1015.jpg");
        var dupeA = File(2, Secondary.Id, @"F:\backup\old\DSC-1015.jpg");
        var dupeB = File(3, Secondary.Id, @"F:\backup\older\DSC-1015.jpg");

        var plan = CreatePlanner().CreatePlan([Group(keeper, dupeA, dupeB)], [Primary, Secondary], ArchiveRoot);

        Assert.Equal(2, plan.FileCount);
        Assert.DoesNotContain(plan.Actions, a => a.File.Id == keeper.Id);
        Assert.All(plan.Actions, a => Assert.Equal(keeper.Id, a.Keeper.Id));
    }

    [Fact]
    public void TwoCopiesInPrimaryRoot_SameDirectory_KeepsShortestName()
    {
        var keeper = File(1, Primary.Id, @"D:\pictures\family\DSC-1015.jpg");
        var dupe = File(2, Primary.Id, @"D:\pictures\family\DSC-1015_001.jpg");

        var plan = CreatePlanner().CreatePlan([Group(keeper, dupe)], [Primary], ArchiveRoot);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(dupe.Id, action.File.Id);
        Assert.Equal(keeper.Id, action.Keeper.Id);
        Assert.Contains("shortest file name", action.Reason);
    }

    [Fact]
    public void TwoCopiesInPrimaryRoot_DifferentDirectories_KeepsDateBasedPath()
    {
        var keeper = File(1, Primary.Id, @"D:\pictures\2024\2024-05-15\DSC-1015.jpg");
        var dupe = File(2, Primary.Id, @"D:\pictures\mypictures\nikon\DSC-1015.jpg");

        var plan = CreatePlanner().CreatePlan([Group(keeper, dupe)], [Primary], ArchiveRoot);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(dupe.Id, action.File.Id);
        Assert.Equal(keeper.Id, action.Keeper.Id);
        Assert.Contains("date-based path", action.Reason);
    }

    [Fact]
    public void DateRuleWins_EvenWhenTheDatedFileHasTheLongerName()
    {
        // Guards against the name-length tie-break being applied before the date rule.
        var keeper = File(1, Primary.Id, @"D:\pictures\2024\2024-05-15\DSC-1015-holiday.jpg");
        var dupe = File(2, Primary.Id, @"D:\pictures\misc\a.jpg");

        var plan = CreatePlanner().CreatePlan([Group(keeper, dupe)], [Primary], ArchiveRoot);

        Assert.Equal(dupe.Id, Assert.Single(plan.Actions).File.Id);
    }

    [Fact]
    public void NoCopyInPrimaryRoot_KeepsFileFromEarliestAddedRoot()
    {
        var third = new RootPath { Id = 3, Path = @"G:\archive", Ordinal = 2 };

        var keeper = File(1, Secondary.Id, @"F:\backup\old\DSC-1015.jpg");
        var dupe = File(2, third.Id, @"G:\archive\DSC-1015.jpg");

        var plan = CreatePlanner().CreatePlan([Group(keeper, dupe)], [Primary, Secondary, third], ArchiveRoot);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(dupe.Id, action.File.Id);
        Assert.Equal(keeper.Id, action.Keeper.Id);
    }

    [Fact]
    public void ArchiveDestination_PreservesPathRelativeToItsOwnRoot()
    {
        var keeper = File(1, Primary.Id, @"D:\pictures\2024\2024-05-15\DSC-1015.jpg");
        var dupe = File(2, Secondary.Id, @"F:\backup\familypictures\DSC-1015.jpg");

        var plan = CreatePlanner().CreatePlan([Group(keeper, dupe)], [Primary, Secondary], ArchiveRoot);

        Assert.Equal(
            Path.GetFullPath(@"E:\duplicates\familypictures\DSC-1015.jpg"),
            Assert.Single(plan.Actions).DestinationPath);
    }

    [Fact]
    public void ThreeCopiesInPrimaryRoot_ArchivesEveryoneExceptOneKeeper()
    {
        var a = File(1, Primary.Id, @"D:\pictures\family\DSC-1015.jpg");
        var b = File(2, Primary.Id, @"D:\pictures\family\DSC-1015_001.jpg");
        var c = File(3, Primary.Id, @"D:\pictures\family\DSC-1015_002.jpg");

        var plan = CreatePlanner().CreatePlan([Group(a, b, c)], [Primary], ArchiveRoot);

        Assert.Equal(2, plan.FileCount);
        Assert.Equal(1, plan.DuplicateGroupCount);
        Assert.DoesNotContain(plan.Actions, x => x.File.Id == a.Id);
    }

    [Fact]
    public void TotalBytes_SumsOnlyArchivedFiles()
    {
        var keeper = File(1, Primary.Id, @"D:\pictures\family\DSC-1015.jpg", 5000);
        var dupeA = File(2, Secondary.Id, @"F:\backup\a\DSC-1015.jpg", 5000);
        var dupeB = File(3, Secondary.Id, @"F:\backup\b\DSC-1015.jpg", 5000);

        var plan = CreatePlanner().CreatePlan([Group(keeper, dupeA, dupeB)], [Primary, Secondary], ArchiveRoot);

        Assert.Equal(10000, plan.TotalBytes);
    }

    [Fact]
    public void GroupWithOnlyOneFile_ProducesNoActions()
    {
        var only = File(1, Primary.Id, @"D:\pictures\family\DSC-1015.jpg");

        var plan = CreatePlanner().CreatePlan([Group(only)], [Primary], ArchiveRoot);

        Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.DuplicateGroupCount);
    }
}
