namespace TimecodeBridge.Core.Models;

public record AudioDeviceInfo(string Id, string DisplayName, bool IsLoopback)
{
    public override string ToString() => DisplayName;
}
