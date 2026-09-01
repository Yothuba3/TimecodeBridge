namespace TimecodeBridge.Core.Models;

public class OscHost
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string IpAddress { get; set; }
    public required int Port { get; set; }
    public bool IsEnabled { get; set; } = true;
}
