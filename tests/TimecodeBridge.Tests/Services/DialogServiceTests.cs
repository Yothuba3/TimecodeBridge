using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Tests.Services;

public class DialogServiceTests
{
    [Fact]
    public void CueDialogService_ICueDialogServiceを実装している()
    {
        Assert.True(typeof(ICueDialogService).IsAssignableFrom(typeof(CueDialogService)));
    }

    [Fact]
    public void HostDialogService_IHostDialogServiceを実装している()
    {
        Assert.True(typeof(IHostDialogService).IsAssignableFrom(typeof(HostDialogService)));
    }

    [Fact]
    public void FileDialogService_IFileDialogServiceを実装している()
    {
        Assert.True(typeof(IFileDialogService).IsAssignableFrom(typeof(FileDialogService)));
    }

    [Fact]
    public void OscTriggerDialogService_IOscTriggerDialogServiceを実装している()
    {
        Assert.True(typeof(IOscTriggerDialogService).IsAssignableFrom(typeof(OscTriggerDialogService)));
    }
}
