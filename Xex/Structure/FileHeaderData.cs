namespace XexTool.Xex.Structure;

/// <summary>
/// File header data entry
/// </summary>
public class FileHeaderData
{
    public uint Offset { get; set; }
    public uint Value { get; set; }
    public string Description { get; set; } = "";

    public FileHeaderData(uint offset, string description)
    {
        Offset = offset;
        Description = description;
    }
}