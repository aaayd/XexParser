namespace XexTool.Xex.Structure;

/// <summary>
/// Library version information
/// </summary>
public struct Library
{
    public string Name;        // 8 characters max
    public ushort Version1;
    public ushort Version2;
    public ushort Version3;
    public ushort Version4;

    public bool IsApproved => (Version4 & 0x8000) == 0;
    public ushort CleanVersion4 => (ushort)(Version4 & ~0x8000);
}