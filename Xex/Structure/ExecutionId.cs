namespace XexTool.Xex.Structure;

/// <summary>
/// Execution ID information
/// </summary>
public class ExecutionId
{
    public uint MediaId { get; set; }
    public uint Version { get; set; }
    public uint BaseVersion { get; set; }
    public uint TitleId { get; set; }
    public byte Platform { get; set; }
    public byte ExecutableType { get; set; }
    public byte DiscNum { get; set; }
    public byte DiscsInSet { get; set; }
    public uint SaveGameId { get; set; }

    public string TitleIdHex => $"{TitleId:X8}";
}
