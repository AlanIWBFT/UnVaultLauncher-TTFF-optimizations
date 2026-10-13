using System.Text.Json;
using System.Text.Json.Serialization;
using UnVault.Core;
using UnVault.Core.Util;

namespace UnVault.App.Services;

/// <summary>One of the user's own categories of projects; a project can be in several.</summary>
public sealed class ProjectCategory
{
    public string Name { get; set; } = "";

    /// <summary>Its projects shown or not, under its heading.</summary>
    public bool Folded { get; set; }

    /// <summary>Its projects' .uproject files. One not found now stays: its drive may be back.</summary>
    public List<string> Projects { get; set; } = [];

    public bool Contains(string projectFile) => Projects.Contains(projectFile, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The Projects tab's categories, kept in %LOCALAPPDATA%\UnVaultLauncher\project-categories.json: the user's own, and
/// whether the projects in none of them are folded.
/// </summary>
public sealed class ProjectCategories
{
    public List<ProjectCategory> Categories { get; set; } = [];

    /// <summary>The Uncategorized group folded.</summary>
    public bool UncategorizedFolded { get; set; }

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "project-categories.json");

    /// <summary>
    /// The file is there but couldn't be read (another program has it open): these categories start empty, and are never
    /// saved over it, which would lose the ones in it.
    /// </summary>
    private bool KeepsFile { get; init; }

    /// <summary>
    /// The saved categories; none when there are none yet. A damaged file (edited by hand, or cut short) is set aside as
    /// {file}.bad, so saving doesn't replace it: the categories in it can still be recovered. One that can't be read now
    /// is left as it is.
    /// </summary>
    public static ProjectCategories Load(string? path = null)
    {
        path ??= FilePath;
        if (!File.Exists(path))
            return new();
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new() { KeepsFile = true };
        }
        try
        {
            var categories = JsonSerializer.Deserialize(text, ProjectCategoriesJSONContext.Default.ProjectCategories)
                ?? throw new JsonException("The file holds no categories.");
            return categories.Tidied();
        }
        catch (JsonException)
        {
            return new() { KeepsFile = !SetAside(path) };
        }
    }

    /// <summary>Moves a damaged file to {file}.bad, replacing one set aside before; false when it can't be moved.</summary>
    private static bool SetAside(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A file edited by hand may leave out a list, or a category's name: lists are filled in, nameless categories dropped.</summary>
    private ProjectCategories Tidied()
    {
        Categories = [.. (Categories ?? []).Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name))];
        foreach (var category in Categories)
        {
            category.Name = category.Name.Trim();
            category.Projects = [.. (category.Projects ?? []).Where(p => !string.IsNullOrWhiteSpace(p))];
        }
        return this;
    }

    /// <summary>Categories that can't be saved stay for this session; the next change tries again.</summary>
    public void Save(string? path = null)
    {
        if (KeepsFile)
            return;
        path ??= FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, ProjectCategoriesJSONContext.Default.ProjectCategories));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Next change.
        }
    }

    public ProjectCategory? Find(string name) =>
        Categories.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProjectCategories))]
internal sealed partial class ProjectCategoriesJSONContext : JsonSerializerContext;
