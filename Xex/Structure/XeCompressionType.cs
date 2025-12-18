namespace XexTool.Xex.Structure;

/// <summary>
/// Compression type enum
/// </summary>
public enum XeCompressionType : ushort
{
    Zeroed = 0,
    Raw = 1,          // Uncompressed (but may be encrypted)
    Compressed = 2,   // LZX compressed
    DeltaCompressed = 3
}