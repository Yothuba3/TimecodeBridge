namespace TimecodeBridge.App.ViewModels;

public record LogEntry(DateTime Timestamp, string Message, bool IsSuccess);
