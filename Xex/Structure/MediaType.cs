namespace XexTool.Xex.Structure;

/// <summary>
/// Media type flags and descriptions
/// </summary>
public class MediaType
{
    public uint Flag { get; }
    public string Description { get; }

    public MediaType(uint flag, string description)
    {
        Flag = flag;
        Description = description;
    }

    public static readonly MediaType[] Types = new[]
    {
        new MediaType(0x00000001, "hard disk"),
        new MediaType(0x00000002, "DVD-X2"),
        new MediaType(0x00000004, "DVD/CD"),
        new MediaType(0x00000008, "DVD-5"),
        new MediaType(0x00000010, "DVD-9"),
        new MediaType(0x00000020, "system flash"),
        new MediaType(0x00000080, "memory unit"),
        new MediaType(0x00000100, "mass storage device"),
        new MediaType(0x00000200, "SMB filesystem"),
        new MediaType(0x00000400, "direct-from-RAM"),
        new MediaType(0x01000000, "insecure package"),
        new MediaType(0x02000000, "save game package"),
        new MediaType(0x04000000, "locally signed package"),
        new MediaType(0x08000000, "Live-signed package"),
        new MediaType(0x10000000, "Xbox platform package")
    };
}