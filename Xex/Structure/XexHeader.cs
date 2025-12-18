namespace XexTool.Xex.Structure;

/// <summary>
/// XEX2 file header structure
/// </summary>
public struct XexHeader
{
    public byte[] Magic;       // 4 bytes, should be "XEX2"
    public uint ModuleFlags;
    public uint DataOffset;
    public uint Reserved;
    public uint FileHeaderOffset;
    public uint OptionalHeaderEntries;
}