namespace XexTool.Xex.Structure;

/// <summary>
/// Optional header info types
/// </summary>
public class InfoType
{
    public uint ID { get; }
    public string Description { get; }
    public bool HasDecoder { get; }

    public InfoType(uint id, string description, bool hasDecoder = false)
    {
        ID = id;
        Description = description;
        HasDecoder = hasDecoder;
    }

    public static readonly InfoType[] InfoTable = new[]
    {
        new InfoType(0x00040006, "Execution Id", true),
        new InfoType(0x00010201, "Image Base Address"),
        new InfoType(0x00010001, "Original Base Address"),
        new InfoType(0x00010100, "Entry Point"),
        new InfoType(0x00018002, "Image Checksum & Image Timestamp"),
        new InfoType(0x000103FF, "Other Import Libraries"),
        new InfoType(0x000200FF, "Library Versions", true),
        new InfoType(0x000002FF, "Resource Section", true),
        new InfoType(0x000003FF, "Decompression Information", true),
        new InfoType(0x00020104, "TLS"),
        new InfoType(0x00020200, "Default Stack Size"),
        new InfoType(0x00020301, "Default File System Cache Size"),
        new InfoType(0x00020401, "Default Heap Size"),
        new InfoType(0x00040201, "Title Workspace Size"),
        new InfoType(0x00030000, "System Flags"),
        new InfoType(0x00018102, "Image Is Enabled For Callcap"),
        new InfoType(0x00018200, "Image Is Enabled For Fastcap"),
        new InfoType(0x00E10402, "Image Includes Export By Name"),
        new InfoType(0x00040310, "Image Game Rating Specified"),
        new InfoType(0x00040404, "LAN Key"),
        new InfoType(0x000080FF, "Bound Pathname", true),
        new InfoType(0x000405FF, "Unknown [1]"),
        new InfoType(0x000183FF, "Unknown [2]")
    };
}