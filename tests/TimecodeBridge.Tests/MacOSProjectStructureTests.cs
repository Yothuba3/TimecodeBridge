using System.Xml.Linq;

namespace TimecodeBridge.Tests;

/// <summary>
/// タスク2.1のTDD: TimecodeBridge.macOS.csprojの構造と設定を検証
/// </summary>
public class MacOSProjectStructureTests
{
    private const string MacOSProjectPath = "../../src/TimecodeBridge.macOS/TimecodeBridge.macOS.csproj";

    [Fact]
    public void MacOSProject_ShouldExist()
    {
        // Arrange
        var projectPath = Path.GetFullPath(MacOSProjectPath);

        // Act & Assert
        Assert.True(File.Exists(projectPath), $"macOSプロジェクトファイルが存在しません: {projectPath}");
    }

    [Fact]
    public void MacOSProject_ShouldTargetNet8()
    {
        // Arrange
        var projectPath = Path.GetFullPath(MacOSProjectPath);
        var doc = XDocument.Load(projectPath);

        // Act
        var targetFramework = doc.Descendants("TargetFramework").FirstOrDefault()?.Value;

        // Assert
        Assert.Equal("net8.0", targetFramework);
    }

    [Fact]
    public void MacOSProject_ShouldReferenceTimecodeBridgeCore()
    {
        // Arrange
        var projectPath = Path.GetFullPath(MacOSProjectPath);
        var doc = XDocument.Load(projectPath);

        // Act
        var coreReference = doc.Descendants("ProjectReference")
            .FirstOrDefault(x => x.Attribute("Include")?.Value.Contains("TimecodeBridge.Core") == true);

        // Assert
        Assert.NotNull(coreReference);
    }

    [Fact]
    public void MacOSProject_ShouldIncludeAvaloniaPackages()
    {
        // Arrange
        var projectPath = Path.GetFullPath(MacOSProjectPath);
        var doc = XDocument.Load(projectPath);
        var packageReferences = doc.Descendants("PackageReference")
            .Select(x => x.Attribute("Include")?.Value)
            .ToList();

        // Act & Assert
        Assert.Contains(packageReferences, p => p?.StartsWith("Avalonia") == true);
        Assert.Contains(packageReferences, p => p == "Avalonia.Themes.Fluent");
    }

    [Fact]
    public void MacOSProject_ShouldIncludeCommunityToolkitMvvm()
    {
        // Arrange
        var projectPath = Path.GetFullPath(MacOSProjectPath);
        var doc = XDocument.Load(projectPath);

        // Act
        var mvvmPackage = doc.Descendants("PackageReference")
            .FirstOrDefault(x => x.Attribute("Include")?.Value == "CommunityToolkit.Mvvm");

        // Assert
        Assert.NotNull(mvvmPackage);
    }

    [Fact]
    public void InfoPlist_ShouldExist()
    {
        // Arrange
        var infoPlistPath = Path.GetFullPath("../../src/TimecodeBridge.macOS/Info.plist");

        // Act & Assert
        Assert.True(File.Exists(infoPlistPath), $"Info.plistが存在しません: {infoPlistPath}");
    }

    [Fact]
    public void InfoPlist_ShouldContainCFBundleIdentifier()
    {
        // Arrange
        var infoPlistPath = Path.GetFullPath("../../src/TimecodeBridge.macOS/Info.plist");
        var content = File.ReadAllText(infoPlistPath);

        // Act & Assert
        Assert.Contains("CFBundleIdentifier", content);
    }

    [Fact]
    public void InfoPlist_ShouldContainNSMicrophoneUsageDescription()
    {
        // Arrange
        var infoPlistPath = Path.GetFullPath("../../src/TimecodeBridge.macOS/Info.plist");
        var content = File.ReadAllText(infoPlistPath);

        // Act & Assert
        Assert.Contains("NSMicrophoneUsageDescription", content);
    }

    [Fact]
    public void MacOSProject_ShouldBuildSuccessfully()
    {
        // Arrange
        var projectPath = Path.GetFullPath(MacOSProjectPath);

        // Act
        var result = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" --nologo --verbosity quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        result?.WaitForExit();
        var exitCode = result?.ExitCode ?? -1;

        // Assert
        Assert.Equal(0, exitCode);
    }
}
