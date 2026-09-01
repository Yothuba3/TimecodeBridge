using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface ITimecodeRelay
{
    string OscAddressPattern { get; set; }
    RelayInterval ContinuousInterval { get; set; }
    IReadOnlyList<string> TargetHostIds { get; set; }
    bool IsContinuousEnabled { get; set; }
    void TriggerOneShot();
}
