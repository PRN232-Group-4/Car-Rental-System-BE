using System.Xml.Linq;
using Xunit;

namespace VehicleRental.Tests;

public sealed class ArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Theory]
    [InlineData("VehicleRental.Domain")]
    [InlineData("VehicleRental.Application", "VehicleRental.Domain")]
    [InlineData("VehicleRental.Infrastructure", "VehicleRental.Application", "VehicleRental.Domain")]
    [InlineData("VehicleRental.WebApi", "VehicleRental.Application", "VehicleRental.Infrastructure")]
    public void Project_dependencies_point_inward(string project, params string[] allowed)
    {
        Assert.Empty(ProjectReferences(project).Except(allowed));
    }

    private static IEnumerable<string> ProjectReferences(string project)
    {
        var csproj = Path.Combine(RepoRoot, "src", project, $"{project}.csproj");
        Assert.True(File.Exists(csproj), $"Missing project file: {csproj}");

        return XDocument.Load(csproj)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension((string?)element.Attribute("Include") ?? string.Empty))
            .Where(name => name.Length > 0);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Car-Rental-System-BE.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the solution root from the test output directory.");
    }
}
