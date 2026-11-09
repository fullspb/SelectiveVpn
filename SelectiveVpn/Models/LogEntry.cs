namespace SelectiveVpn.Models;

public sealed class LogEntry
{
    public LogEntry(string message)
    {
        Timestamp = DateTime.Now;
        Message = message;
    }

    public DateTime Timestamp { get; }

    public string Message { get; }

    public string Display => $"{Timestamp:HH:mm:ss}  {Message}";
}
