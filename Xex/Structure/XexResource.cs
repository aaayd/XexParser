namespace XexTool.Xex.Structure;

/// <summary>
/// XEX Resource entry (for embedded images, etc.)
/// </summary>
public class XexResource
{
    public string Name { get; set; } = "";
    public uint Offset { get; set; }
    public uint Size { get; set; }
    public byte[]? Data { get; set; }
    public string Type { get; set; } = "Unknown";
}