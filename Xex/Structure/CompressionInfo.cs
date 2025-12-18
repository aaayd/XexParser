namespace XexTool.Xex.Structure;

public class CompressionInfo
{
    public uint InfoSize { get; set; }
    public XeEncryptionType EncryptionType { get; set; }
    public XeCompressionType CompressionType { get; set; }
    public uint CompressionWindow { get; set; }
    public uint BlockSize { get; set; }
    public byte[] Hash { get; set; } = new byte[20];  // 20 bytes SHA1 hash
    public byte[] RawData { get; set; } = Array.Empty<byte>(); // Raw header data for debugging
}