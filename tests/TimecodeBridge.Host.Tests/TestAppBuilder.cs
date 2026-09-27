using Avalonia;
using Avalonia.Headless;
using TimecodeBridge.Host.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// HostState は Avalonia の Dispatcher に依存するため、UI スレッドを共有するテストを並列にしない
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace TimecodeBridge.Host.Tests;

public sealed class TestApp : Application { }

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
