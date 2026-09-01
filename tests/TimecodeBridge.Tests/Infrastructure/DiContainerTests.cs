using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Tests.Infrastructure;

public class DiContainerTests
{
    private readonly IServiceProvider _serviceProvider;

    public DiContainerTests()
    {
        var services = new ServiceCollection();
        ServiceRegistration.ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(typeof(ITimecodeEngine))]
    [InlineData(typeof(ICueManager))]
    [InlineData(typeof(IOscSender))]
    [InlineData(typeof(ITimecodeRelay))]
    [InlineData(typeof(IHostRegistry))]
    [InlineData(typeof(IProjectService))]
    [InlineData(typeof(IAppSettingsService))]
    [InlineData(typeof(IRecentProjectsService))]
    [InlineData(typeof(IOscTriggerPanelManager))]
    [InlineData(typeof(IOscTriggerDialogService))]
    public void Service_ShouldBeResolvable(Type serviceType)
    {
        var service = _serviceProvider.GetService(serviceType);
        Assert.NotNull(service);
    }

    [Theory]
    [InlineData(typeof(MainViewModel))]
    [InlineData(typeof(TimecodeViewModel))]
    [InlineData(typeof(CueListViewModel))]
    [InlineData(typeof(HostManagerViewModel))]
    [InlineData(typeof(RelayViewModel))]
    [InlineData(typeof(LogViewModel))]
    [InlineData(typeof(OscTriggerPanelViewModel))]
    public void ViewModel_ShouldBeResolvable(Type viewModelType)
    {
        var viewModel = _serviceProvider.GetService(viewModelType);
        Assert.NotNull(viewModel);
    }

    [Fact]
    public void HostRegistry_ShouldBeSingleton()
    {
        var instance1 = _serviceProvider.GetService<IHostRegistry>();
        var instance2 = _serviceProvider.GetService<IHostRegistry>();
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void TimecodeEngine_ShouldBeSingleton()
    {
        var instance1 = _serviceProvider.GetService<ITimecodeEngine>();
        var instance2 = _serviceProvider.GetService<ITimecodeEngine>();
        Assert.Same(instance1, instance2);
    }
}
