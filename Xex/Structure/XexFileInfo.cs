namespace XexTool.Xex.Structure;

/// <summary>
/// Parsed XEX file information for display
/// </summary>
public class XexFileInfo
{
    public string Magic { get; set; } = "";
    public uint ModuleFlags { get; set; }
    public uint DataOffset { get; set; }
    public uint Reserved { get; set; }
    public uint FileHeaderOffset { get; set; }
    public uint OptionalHeaderEntries { get; set; }

    // File header values
    public uint LoadAddress { get; set; }
    public uint ImageSize { get; set; }
    public uint GameRegion { get; set; }
    public uint ImageFlags { get; set; }
    public uint AllowedMediaTypes { get; set; }

    // Optional headers
    public List<OptionalHeaderInfo> OptionalHeaders { get; } = new();

    // Libraries
    public List<Library> Libraries { get; } = new();

    // Media types
    public List<string> MediaTypes { get; } = new();

    // Bound pathname
    public string? BoundPathname { get; set; }

    // Compression info
    public CompressionInfo? Compression { get; set; }

    // Session key (decrypted)
    public byte[]? SessionKey { get; set; }

    // Image base address (used for calculating resource offsets)
    public uint ImageBaseAddress { get; set; }

    // Resource section offset
    public uint ResourceSectionOffset { get; set; }

    // Embedded resources (images, etc.)
    public List<XexResource> Resources { get; } = new();

    // Execution ID
    public ExecutionId? ExecutionInfo { get; set; }

    // Game title (extracted from XDBF resource)
    public string? GameTitle { get; set; }
}