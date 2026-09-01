using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Services;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Tests.Services;

/// <summary>
/// App.ConfigureServices の登録で MainViewModel まで解決できることを検証する
/// </summary>
public class DiContainerTests
{
    [Fact]
    public void ConfigureServices_MainViewModelと子ViewModelを解決できる()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var main = provider.GetRequiredService<MainViewModel>();

        Assert.Same(provider.GetRequiredService<TimecodeViewModel>(), main.TimecodeViewModel);
        Assert.Same(provider.GetRequiredService<CueListViewModel>(), main.CueListViewModel);
        Assert.Same(provider.GetRequiredService<RelayViewModel>(), main.RelayViewModel);
        Assert.Same(provider.GetRequiredService<HostManagerViewModel>(), main.HostManagerViewModel);
        Assert.Same(provider.GetRequiredService<LogViewModel>(), main.LogViewModel);
    }

    [Fact]
    public void ConfigureServices_macOS固有サービスが登録されている()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<TimecodeEngine>(provider.GetRequiredService<ITimecodeEngine>());
        Assert.IsType<CueDialogService>(provider.GetRequiredService<ICueDialogService>());
        Assert.IsType<HostDialogService>(provider.GetRequiredService<IHostDialogService>());
        Assert.IsType<RecentProjectsService>(provider.GetRequiredService<TimecodeBridge.Services.Interfaces.IRecentProjectsService>());
        Assert.NotNull(provider.GetRequiredService<ITimecodeRelay>());
        Assert.NotNull(provider.GetRequiredService<ICueManager>());
        Assert.NotNull(provider.GetRequiredService<RelayViewModel>());
        Assert.NotNull(provider.GetRequiredService<AudioWaveformViewModel>());
    }
}
