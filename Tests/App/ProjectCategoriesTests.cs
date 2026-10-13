using UnVault.App.Services;

namespace UnVault.App.Tests;

/// <summary>The file the project categories are kept in: whatever is wrong with it, the categories in it aren't lost.</summary>
public sealed class ProjectCategoriesTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"unvault-categories-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_file);
        File.Delete(_file + ".bad");
    }

    private static ProjectCategories With(string name) =>
        new() { Categories = [new ProjectCategory { Name = name, Projects = [@"D:\Projects\Hillside\Hillside.uproject"] }] };

    [Theory]
    [InlineData("{ \"Categories\": [ { \"Name\": \"Demos\", \"Proj")] // cut short
    [InlineData("null")]
    public void A_damaged_file_is_set_aside_rather_than_saved_over(string damaged)
    {
        File.WriteAllText(_file, damaged);

        var categories = ProjectCategories.Load(_file);
        Assert.Empty(categories.Categories);
        Assert.Equal(damaged, File.ReadAllText(_file + ".bad"));

        // Starting afresh: the new categories are saved, the damaged ones kept beside them to be recovered.
        categories.Categories.Add(With("Plugins").Categories[0]);
        categories.Save(_file);
        Assert.Equal(["Plugins"], ProjectCategories.Load(_file).Categories.Select(c => c.Name));
        Assert.Equal(damaged, File.ReadAllText(_file + ".bad"));
    }

    [Fact]
    public void A_hand_edited_file_with_gaps_still_loads()
    {
        File.WriteAllText(_file, """{ "Categories": [ { "Name": " Demos ", "Projects": null }, { "Name": "" }, null ] }""");

        var categories = ProjectCategories.Load(_file);

        var demos = Assert.Single(categories.Categories);
        Assert.Equal(("Demos", 0), (demos.Name, demos.Projects.Count));
        Assert.False(File.Exists(_file + ".bad")); // nothing in it was lost
    }

    /// <summary>Another program has the file open: it can't be read now, but there's nothing wrong with it, so it's left alone.</summary>
    [Fact]
    public void A_file_that_cant_be_read_now_is_never_saved_over()
    {
        With("Demos").Save(_file);
        ProjectCategories categories;
        using (new FileStream(_file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            categories = ProjectCategories.Load(_file);
        Assert.Empty(categories.Categories);

        categories.Categories.Add(With("Plugins").Categories[0]);
        categories.Save(_file);

        Assert.Equal(["Demos"], ProjectCategories.Load(_file).Categories.Select(c => c.Name));
        Assert.False(File.Exists(_file + ".bad"));
    }
}
