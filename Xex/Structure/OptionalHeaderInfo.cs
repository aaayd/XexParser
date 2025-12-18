namespace XexTool.Xex.Structure;

/// <summary>
/// Optional header info for display
/// </summary>
public class OptionalHeaderInfo
{
    public uint ID { get; set; }
    public string Description { get; set; } = "";
    public uint Data { get; set; }
    public string? DecodedValue { get; set; }
}