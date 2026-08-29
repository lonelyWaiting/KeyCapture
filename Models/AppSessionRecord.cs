namespace KeyCapture.Models;

public class AppSessionRecord
{
    public long Id { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime LastActiveTime { get; set; }
    public int KeyCount { get; set; }
}
