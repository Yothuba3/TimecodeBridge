using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.ViewModels;

public record LogEntry(DateTime Timestamp, string Message, bool IsSuccess);
