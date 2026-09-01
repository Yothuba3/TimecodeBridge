using Avalonia;
using Avalonia.Headless;
using TimecodeBridge.macOS.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace TimecodeBridge.macOS.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
