namespace KeyCapture.Models;

public class KeyEventRecord
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int VirtualKeyCode { get; set; }
    public string KeyDisplayText { get; set; } = string.Empty;
    public string Modifiers { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public bool IsCombo { get; set; }
}
