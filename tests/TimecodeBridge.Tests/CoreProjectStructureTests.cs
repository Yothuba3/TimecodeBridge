using System.IO;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Xml.Linq;
using Xunit;

namespace TimecodeBridge.Tests;

/// <summary>
/// Test-Driven Development: TimecodeBridge.Core プロジェクト構造の検証テスト
/// </summary>
public class CoreProjectStructureTests
{
    private const string CoreProjectPath = "../../../src/TimecodeBridge.Core/TimecodeBridge.Core.csproj";
    private const string SolutionPath = "../../../TimecodeBridge.sln";

    [Fact]
    public void CoreProject_ShouldExist()
    {
        // Arrange & Act
        var projectExists = File.Exists(CoreProjectPath);

        // Assert
        Assert.True(projectExists, $"TimecodeBridge.Core.csproj が存在しません: {Path.GetFullPath(CoreProjectPath)}");
    }

    [Fact]
    public void CoreProject_ShouldTargetNet80()
    {
        // Arrange
        var projectFile = XDocument.Load(CoreProjectPath);
        var targetFramework = projectFile.Descendants("TargetFramework").FirstOrDefault()?.Value;

        // Assert
        Assert.Equal("net8.0", targetFramework);
    }

    [Fact]
    public void CoreProject_ShouldNotTargetWindowsSpecificFramework()
    {
        // Arrange
        var projectFile = XDocument.Load(CoreProjectPath);
        var targetFramework = projectFile.Descendants("TargetFramework").FirstOrDefault()?.Value;

        // Assert
        Assert.DoesNotContain("-windows", targetFramework ?? string.Empty);
    }

    [Fact]
    public void CoreProject_ShouldHaveRequiredNuGetPackages()
    {
        // Arrange
        var projectFile = XDocument.Load(CoreProjectPath);
        var packageReferences = projectFile.Descendants("PackageReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .ToList();

        var requiredPackages = new[]
        {
            "CommunityToolkit.Mvvm",
            "System.Text.Json"
        };

        // Assert
        foreach (var requiredPackage in requiredPackages)
        {
            Assert.Contains(requiredPackage, packageReferences);
        }
    }

    [Fact]
    public void CoreProject_ShouldNotHavePlatformSpecificPackages()
    {
        // Arrange
        var projectFile = XDocument.Load(CoreProjectPath);
        var packageReferences = projectFile.Descendants("PackageReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .ToList();

        var platformSpecificPackages = new[] { "NAudio" };

        // Assert
        foreach (var platformPackage in platformSpecificPackages)
        {
            Assert.DoesNotContain(platformPackage, packageReferences);
        }
    }

    [Fact]
    public void CoreProject_ShouldHaveModelsDirectory()
    {
        // Arrange
        var modelsDir = Path.Combine(Path.GetDirectoryName(CoreProjectPath)!, "Models");

        // Act
        var directoryExists = Directory.Exists(modelsDir);

        // Assert
        Assert.True(directoryExists, "Models ディレクトリが存在しません");
    }

    [Fact]
    public void CoreProject_ShouldHaveServicesDirectory()
    {
        // Arrange
        var servicesDir = Path.Combine(Path.GetDirectoryName(CoreProjectPath)!, "Services");

        // Act
        var directoryExists = Directory.Exists(servicesDir);

        // Assert
        Assert.True(directoryExists, "Services ディレクトリが存在しません");
    }

    [Fact]
    public void CoreProject_ShouldHaveInterfacesDirectory()
    {
        // Arrange
        var interfacesDir = Path.Combine(Path.GetDirectoryName(CoreProjectPath)!, "Services", "Interfaces");

        // Act
        var directoryExists = Directory.Exists(interfacesDir);

        // Assert
        Assert.True(directoryExists, "Services/Interfaces ディレクトリが存在しません");
    }

    [Fact]
    public void CoreProject_ShouldBeAddedToSolution()
    {
        // Arrange
        var solutionContent = File.ReadAllText(SolutionPath);

        // Assert
        Assert.Contains("TimecodeBridge.Core", solutionContent);
    }

    [Fact]
    public void CoreProject_ShouldEnableNullable()
    {
        // Arrange
        var projectFile = XDocument.Load(CoreProjectPath);
        var nullable = projectFile.Descendants("Nullable").FirstOrDefault()?.Value;

        // Assert
        Assert.Equal("enable", nullable);
    }

    [Fact]
    public void CoreProject_ShouldEnableImplicitUsings()
    {
        // Arrange
        var projectFile = XDocument.Load(CoreProjectPath);
        var implicitUsings = projectFile.Descendants("ImplicitUsings").FirstOrDefault()?.Value;

        // Assert
        Assert.Equal("enable", implicitUsings);
    }

    [Fact]
    public void WindowsProject_ShouldReferenceCore()
    {
        // Arrange
        const string windowsProjectPath = "../../../src/TimecodeBridge/TimecodeBridge.csproj";
        var projectFile = XDocument.Load(windowsProjectPath);
        var projectReferences = projectFile.Descendants("ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .ToList();

        // Assert
        Assert.Contains(projectReferences,
            pr => pr != null && pr.Contains("TimecodeBridge.Core.csproj"));
    }
}
