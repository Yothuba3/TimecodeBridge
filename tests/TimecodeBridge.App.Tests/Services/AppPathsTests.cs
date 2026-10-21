using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Tests.Services;

public class AppPathsTests
{
    [Fact]
    public void DataDirectoryFor_UsesOverrideParentForBothCurrentAndLegacyFolders()
    {
        var previous = Environment.GetEnvironmentVariable(AppPaths.OverrideVariable);
        var parent = Path.Combine(Path.GetTempPath(), "tcb2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, parent);

            Assert.Equal(Path.Combine(parent, AppPaths.AppFolderName), AppPaths.DataDirectory);
            Assert.Equal(Path.Combine(parent, AppPaths.LegacyAppFolderName), AppPaths.DataDirectoryFor(AppPaths.LegacyAppFolderName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, previous);
        }
    }

    [Fact]
    public void DataDirectoryFor_WithoutOverride_StaysUnderTheUserProfile()
    {
        var previous = Environment.GetEnvironmentVariable(AppPaths.OverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, null);

            var directory = AppPaths.DataDirectory;

            Assert.EndsWith(AppPaths.AppFolderName, directory);
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), directory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, previous);
        }
    }
}
