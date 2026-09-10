using Avalonia;
using Avalonia.Headless;
using TimecodeBridge.Host.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace TimecodeBridge.Host.Tests;

public sealed class TestApp : Application { }

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
