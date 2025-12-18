using System.IO;
using System.Security.Cryptography;
using System.Text;
using XexTool.Xex.Structure;

namespace XexTool.Xex;

/// <summary>
/// XEX2 file reader - parses Xbox 360 XEX2 executables
/// </summary>
public class XexReader : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly BinaryReader _reader;
    private byte[] _lastCipherTextBlock = new byte[16];
    private CompressionInfo? _compressionInfo;

    public event Action<string>? OnLog;

    public XexReader(string filePath)
    {
        _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        _reader = new BinaryReader(_fileStream);
    }

    public void Dispose()
    {
        _reader.Dispose();
        _fileStream.Dispose();
    }

    /// <summary>
    /// Swap bytes from big-endian to little-endian (uint32)
    /// </summary>
    private static uint SwapEndian(byte[] bytes, int offset = 0)
    {
        return ((uint)bytes[offset] << 24) |
               ((uint)bytes[offset + 1] << 16) |
               ((uint)bytes[offset + 2] << 8) |
               bytes[offset + 3];
    }

    /// <summary>
    /// Swap bytes in a block from big-endian to little-endian
    /// </summary>
    private static void SwapBlock(byte[] data, int size, int startOffset = 0)
    {
        for (int i = 0; i < (size - startOffset); i += 4)
        {
            int pos = startOffset + i;
            if (pos + 3 < data.Length)
            {
                byte temp = data[pos];
                data[pos] = data[pos + 3];
                data[pos + 3] = temp;
                temp = data[pos + 1];
                data[pos + 1] = data[pos + 2];
                data[pos + 2] = temp;
            }
        }
    }

    /// <summary>
    /// Read bytes from file and swap endianness
    /// </summary>
    private byte[] ReadAndSwap(int size, int swapOffset = 0)
    {
        byte[] data = _reader.ReadBytes(size);
        SwapBlock(data, size, swapOffset);
        return data;
    }

    /// <summary>
    /// Read a uint32 and swap endianness
    /// </summary>
    private uint ReadUInt32BigEndian()
    {
        byte[] bytes = _reader.ReadBytes(4);
        return SwapEndian(bytes);
    }

    /// <summary>
    /// Read a ushort and swap endianness
    /// </summary>
    private ushort ReadUInt16BigEndian()
    {
        byte[] bytes = _reader.ReadBytes(2);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    /// <summary>
    /// Pad a string to a specific length
    /// </summary>
    private static string Pad(byte[] data, int maxLength)
    {
        int length = 0;
        for (int i = 0; i < Math.Min(data.Length, maxLength); i++)
        {
            if (data[i] == 0) break;
            length++;
        }
        return Encoding.ASCII.GetString(data, 0, length).PadRight(maxLength);
    }

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }

    /// <summary>
    /// Parse the XEX file and return file information
    /// </summary>
    public XexFileInfo Parse()
    {
        var info = new XexFileInfo();

        // Read header
        _fileStream.Seek(0, SeekOrigin.Begin);

        var header = new XexHeader
        {
            Magic = _reader.ReadBytes(4),
            ModuleFlags = ReadUInt32BigEndian(),
            DataOffset = ReadUInt32BigEndian(),
            Reserved = ReadUInt32BigEndian(),
            FileHeaderOffset = ReadUInt32BigEndian(),
            OptionalHeaderEntries = ReadUInt32BigEndian()
        };

        info.Magic = Encoding.ASCII.GetString(header.Magic);
        info.ModuleFlags = header.ModuleFlags;
        info.DataOffset = header.DataOffset;
        info.Reserved = header.Reserved;
        info.FileHeaderOffset = header.FileHeaderOffset;
        info.OptionalHeaderEntries = header.OptionalHeaderEntries;

        Log($"Magic: '{info.Magic}'");
        Log($"ModuleFlags: {info.ModuleFlags}");
        Log($"DataOffset: {info.DataOffset}");
        Log($"Reserved: {info.Reserved}");
        Log($"FileHeaderOffset: {info.FileHeaderOffset}");
        Log($"OptionalHeaderEntries: {info.OptionalHeaderEntries}");

        // Validate magic
        if (info.Magic != "XEX2")
        {
            throw new InvalidDataException("Not a valid XEX2 executable!");
        }

        // Read file header values
        var fileHeaderData = new FileHeaderData[]
        {
            new(0x00000000, "module flags"),
            new(0x00000110, "load address"),
            new(0x00000004, "image size"),
            new(0x00000178, "game region"),
            new(0x0000010C, "image flags"),
            new(0x0000017C, "allowed media types")
        };

        long currentPos = _fileStream.Position;

        Log("\nFILE HEADER");
        foreach (var data in fileHeaderData)
        {
            _fileStream.Seek(data.Offset + header.FileHeaderOffset, SeekOrigin.Begin);
            data.Value = ReadUInt32BigEndian();
            Log($"{data.Description}: 0x{data.Value:X8}");
        }

        info.ModuleFlags = fileHeaderData[0].Value;
        info.LoadAddress = fileHeaderData[1].Value;
        info.ImageSize = fileHeaderData[2].Value;
        info.GameRegion = fileHeaderData[3].Value;
        info.ImageFlags = fileHeaderData[4].Value;
        info.AllowedMediaTypes = fileHeaderData[5].Value;

        // Parse media types
        foreach (var mediaType in MediaType.Types)
        {
            if ((info.AllowedMediaTypes & mediaType.Flag) != 0)
            {
                info.MediaTypes.Add(mediaType.Description);
                Log($"    {mediaType.Description}");
            }
        }

        // Return to position after header
        _fileStream.Seek(currentPos, SeekOrigin.Begin);

        Log("\nOPTIONAL HEADER VALUES");

        // First pass: Read all optional headers and capture important values
        // We need to do this in two passes because the image base address may come
        // after the resource section in the header list
        uint resourceSectionOffset = 0;

        for (int i = 0; i < header.OptionalHeaderEntries; i++)
        {
            var entry = new OptionalHeaderEntry
            {
                ID = ReadUInt32BigEndian(),
                Data = ReadUInt32BigEndian()
            };

            // Find matching info type
            string description = "???";
            InfoType? matchedType = null;
            foreach (var infoType in InfoType.InfoTable)
            {
                if (entry.ID == infoType.ID)
                {
                    description = infoType.Description;
                    matchedType = infoType;
                    break;
                }
            }

            var headerInfo = new OptionalHeaderInfo
            {
                ID = entry.ID,
                Description = description,
                Data = entry.Data
            };

            Log($"0x{entry.ID:X8} {description} 0x{entry.Data:X}");

            long savedPos = _fileStream.Position;

            // Capture image base address (needed for resource offset calculation)
            if (entry.ID == 0x00010201)
            {
                info.ImageBaseAddress = entry.Data;
                headerInfo.DecodedValue = $"0x{entry.Data:X8}";
                Log($"    Image base address: 0x{entry.Data:X8}");
            }

            // Decode special entries (but defer resource section)
            if (matchedType?.HasDecoder == true)
            {
                try
                {
                    switch (entry.ID)
                    {
                        case 0x000080FF: // Bound pathname
                            headerInfo.DecodedValue = DecodeString(entry.Data);
                            info.BoundPathname = headerInfo.DecodedValue;
                            break;
                        case 0x000200FF: // Library versions
                            DecodeLibraries(entry.Data, info.Libraries);
                            break;
                        case 0x000003FF: // Decompression information
                            _compressionInfo = DecodeCompression(entry.Data);
                            info.Compression = _compressionInfo;
                            break;
                        case 0x000002FF: // Resource Section - defer until we have image base address
                            info.ResourceSectionOffset = entry.Data;
                            resourceSectionOffset = entry.Data;
                            Log($"    (Resource section will be decoded after all headers are read)");
                            break;
                        case 0x00040006: // Execution ID
                            info.ExecutionInfo = DecodeExecutionId(entry.Data);
                            if (info.ExecutionInfo != null)
                            {
                                headerInfo.DecodedValue = $"Title ID: {info.ExecutionInfo.TitleIdHex}";
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"    Error decoding: {ex.Message}");
                }
            }

            info.OptionalHeaders.Add(headerInfo);
            _fileStream.Seek(savedPos, SeekOrigin.Begin);
        }

        // Second pass: Now decode the resource section with the correct image base address
        if (resourceSectionOffset > 0)
        {
            Log($"\nDECODING RESOURCE SECTION (with image base 0x{info.ImageBaseAddress:X8})");
            DecodeResourceSection(resourceSectionOffset, info.Resources, info.ImageBaseAddress, info.DataOffset);
        }

        // Read and decrypt session key
        _fileStream.Seek(header.FileHeaderOffset + 0x150, SeekOrigin.Begin);
        byte[] aesKeyEncrypted = _reader.ReadBytes(16);

        // Decrypt with null key
        byte[] aesKey = DecryptSessionKey(aesKeyEncrypted);
        info.SessionKey = aesKey;

        Log("\nsession key: " + BitConverter.ToString(aesKey).Replace("-", " "));

        // Try to extract game title from XDBF in the file
        info.GameTitle = TryExtractGameTitle(info);
        if (!string.IsNullOrEmpty(info.GameTitle))
        {
            Log($"\nGame Title: {info.GameTitle}");
        }

        return info;
    }

    /// <summary>
    /// Try to extract the game title from XDBF data in the XEX file
    /// </summary>
    private string? TryExtractGameTitle(XexFileInfo info)
    {
        try
        {
            // Read a portion of the file starting from the data section
            // The XDBF is typically at the start of the PE data or within the first few MB
            _fileStream.Seek(info.DataOffset, SeekOrigin.Begin);

            int bytesToRead = (int)Math.Min(_fileStream.Length - info.DataOffset, 2 * 1024 * 1024); // Max 2MB
            byte[] data = _reader.ReadBytes(bytesToRead);

            // Try to find and parse XDBF
            uint titleId = info.ExecutionInfo?.TitleId ?? 0;
            string? title = XdbfParser.ScanForGameTitle(data, titleId);

            if (!string.IsNullOrEmpty(title))
                return title;

            // If that didn't work and we have PE-embedded resources, we might need to
            // extract the PE first. For now, return null.
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string DecodeString(uint dataOffset)
    {
        _fileStream.Seek(dataOffset, SeekOrigin.Begin);
        uint length = ReadUInt32BigEndian();

        byte[] strBytes = _reader.ReadBytes((int)length);
        string result = Encoding.ASCII.GetString(strBytes).TrimEnd('\0');
        Log($"    {result}");
        return result;
    }

    private void DecodeLibraries(uint dataOffset, List<Library> libraries)
    {
        _fileStream.Seek(dataOffset, SeekOrigin.Begin);
        uint size = ReadUInt32BigEndian();

        int libSize = 8 + 8; // 8 bytes name + 8 bytes versions
        int count = (int)(size / libSize);

        for (int i = 0; i < count; i++)
        {
            byte[] nameBytes = _reader.ReadBytes(8);
            string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

            var lib = new Library
            {
                Name = name,
                Version1 = ReadUInt16BigEndian(),
                Version2 = ReadUInt16BigEndian(),
                Version3 = ReadUInt16BigEndian(),
                Version4 = ReadUInt16BigEndian()
            };

            libraries.Add(lib);

            string approved = lib.IsApproved ? "approved" : "unapproved";
            Log($"    [\"{lib.Name.PadRight(8)}\", {lib.Version1}.{lib.Version2}.{lib.Version3}.{lib.CleanVersion4} ({approved})]");
        }
    }

    private void DecodeResourceSection(uint dataOffset, List<XexResource> resources, uint imageBaseAddress, uint xexDataOffset)
    {
        _fileStream.Seek(dataOffset, SeekOrigin.Begin);

        // Resource section header
        uint size = ReadUInt32BigEndian();
        Log($"    Resource section size: 0x{size:X}");
        Log($"    Image base address: 0x{imageBaseAddress:X}");
        Log($"    XEX data offset: 0x{xexDataOffset:X}");

        if (size < 4)
        {
            Log($"    Resource section too small, scanning file for images...");
            ScanEntireFileForImages(resources);
            return;
        }

        // Parse resource entries
        // Format: 8 bytes name + 4 bytes address + 4 bytes size = 16 bytes per entry
        int entrySize = 16;
        int numEntries = (int)((size - 4) / entrySize);

        Log($"    Number of resource entries: {numEntries}");

        bool hasEmbeddedPeResources = false;

        for (int i = 0; i < numEntries; i++)
        {
            byte[] nameBytes = _reader.ReadBytes(8);
            string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

            uint resourceAddress = ReadUInt32BigEndian();
            uint resourceSize = ReadUInt32BigEndian();

            Log($"    Resource [{i}]: \"{name}\" at 0x{resourceAddress:X}, size 0x{resourceSize:X}");

            // Check if this resource is inside the PE image (virtual address >= image base)
            if (imageBaseAddress > 0 && resourceAddress >= imageBaseAddress)
            {
                // Calculate the expected file offset
                long expectedOffset = (long)(resourceAddress - imageBaseAddress) + xexDataOffset;

                if (expectedOffset > _fileStream.Length)
                {
                    // Resource is inside the PE image and needs to be extracted after PE decryption
                    Log($"      -> Resource is inside PE image (offset 0x{expectedOffset:X} > file size 0x{_fileStream.Length:X})");
                    Log($"      -> To extract this resource, first use 'Extract PE' to decrypt/decompress the executable");
                    hasEmbeddedPeResources = true;

                    // Store metadata about the resource for later extraction from PE
                    resources.Add(new XexResource
                    {
                        Name = string.IsNullOrEmpty(name) ? $"resource_{i}" : name,
                        Offset = resourceAddress,
                        Size = resourceSize,
                        Data = null, // Will be extracted from PE
                        Type = "PE_EMBEDDED"
                    });
                    continue;
                }
            }

            // Only try to extract if we have valid address and size
            if (resourceAddress > 0 && resourceSize > 0 && resourceSize < 10 * 1024 * 1024)
            {
                long savedPos = _fileStream.Position;

                try
                {
                    // The resource address is a virtual address relative to the image base
                    // Calculate file offset: virtualAddress - imageBaseAddress + dataOffset
                    byte[]? resourceData = TryReadResourceData(resourceAddress, resourceSize, imageBaseAddress, xexDataOffset);

                    if (resourceData != null)
                    {
                        string type = DetectImageType(resourceData);
                        resources.Add(new XexResource
                        {
                            Name = string.IsNullOrEmpty(name) ? $"resource_{i}" : name,
                            Offset = resourceAddress,
                            Size = resourceSize,
                            Data = resourceData,
                            Type = type
                        });
                        Log($"      -> Extracted {resourceData.Length} bytes, type: {type}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"      -> Failed to extract: {ex.Message}");
                }

                _fileStream.Seek(savedPos, SeekOrigin.Begin);
            }
        }

        // If we have PE-embedded resources, tell the user
        if (hasEmbeddedPeResources)
        {
            Log($"    NOTE: Some resources are embedded in the PE image and require PE extraction first");
        }

        // If no extractable resources found, scan the file for embedded images
        if (!resources.Any(r => r.Data != null))
        {
            Log($"    No directly extractable resources found, scanning file for images...");
            ScanEntireFileForImages(resources);
        }
    }

    private byte[]? TryReadResourceData(uint resourceAddress, uint resourceSize, uint imageBaseAddress, uint xexDataOffset)
    {
        // Try multiple approaches to locate the resource data

        // Approach 1: Calculate file offset from virtual address
        // File offset = (virtualAddress - imageBaseAddress) + dataOffset
        if (imageBaseAddress > 0 && resourceAddress >= imageBaseAddress)
        {
            long fileOffset = (long)(resourceAddress - imageBaseAddress) + xexDataOffset;
            Log($"      Trying calculated offset: 0x{fileOffset:X} (VA 0x{resourceAddress:X} - base 0x{imageBaseAddress:X} + data 0x{xexDataOffset:X})");

            if (fileOffset >= 0 && fileOffset + resourceSize <= _fileStream.Length)
            {
                _fileStream.Seek(fileOffset, SeekOrigin.Begin);
                byte[] data = _reader.ReadBytes((int)resourceSize);

                // Check if this looks like valid image data
                string type = DetectImageType(data);
                if (type != "Unknown")
                {
                    return data;
                }

                // Check for XPR2/XPR0 magic
                if (data.Length >= 4 && data[0] == 'X' && data[1] == 'P' && data[2] == 'R')
                {
                    return data;
                }

                // Even if we don't recognize the format, return the data if it seems valid
                // The user can export and examine it
                if (data.Length > 16 && !IsAllZeros(data))
                {
                    return data;
                }
            }
        }

        // Approach 2: Address might be direct file offset
        if (resourceAddress < _fileStream.Length && resourceAddress + resourceSize <= _fileStream.Length)
        {
            _fileStream.Seek(resourceAddress, SeekOrigin.Begin);
            byte[] data = _reader.ReadBytes((int)resourceSize);

            // Check if this looks like valid image data
            string type = DetectImageType(data);
            if (type != "Unknown")
            {
                return data;
            }

            // Check for XPR2/XPR0 magic
            if (data.Length >= 4 && data[0] == 'X' && data[1] == 'P' && data[2] == 'R')
            {
                return data;
            }
        }

        // Approach 3: Scan for image signatures near the expected relative position
        _fileStream.Seek(0, SeekOrigin.Begin);
        byte[] fileData = _reader.ReadBytes((int)Math.Min(_fileStream.Length, 50 * 1024 * 1024));

        byte[] pngSig = { 0x89, 0x50, 0x4E, 0x47 };
        byte[] xprSig = { 0x58, 0x50, 0x52 };

        // Search in a range around where the resource might be
        int searchStart = Math.Max(0, (int)(resourceAddress & 0x00FFFFFF) - 0x1000);
        int searchEnd = Math.Min(fileData.Length - 4, searchStart + 0x10000);

        for (int i = searchStart; i < searchEnd; i++)
        {
            if (MatchesSignature(fileData, i, pngSig))
            {
                int pngEnd = FindPngEnd(fileData, i);
                if (pngEnd > i)
                {
                    int size = pngEnd - i;
                    byte[] pngData = new byte[size];
                    Array.Copy(fileData, i, pngData, 0, size);
                    return pngData;
                }
            }
            else if (i + 3 < fileData.Length && fileData[i] == 'X' && fileData[i + 1] == 'P' && fileData[i + 2] == 'R')
            {
                // XPR2 or XPR0 found
                if (i + 8 < fileData.Length)
                {
                    uint xprSize = BitConverter.ToUInt32(fileData, i + 4);
                    if (xprSize > 0 && xprSize < 5 * 1024 * 1024 && i + xprSize <= fileData.Length)
                    {
                        byte[] xprData = new byte[xprSize];
                        Array.Copy(fileData, i, xprData, 0, (int)xprSize);
                        return xprData;
                    }
                }
            }
        }

        return null;
    }

    private static bool IsAllZeros(byte[] data)
    {
        for (int i = 0; i < Math.Min(data.Length, 256); i++)
        {
            if (data[i] != 0) return false;
        }
        return true;
    }

    private void ScanEntireFileForImages(List<XexResource> resources)
    {
        // Image signatures
        byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG
        byte[] jpegSignature = { 0xFF, 0xD8, 0xFF }; // JPEG
        byte[] xpr2Signature = { 0x58, 0x50, 0x52, 0x32 }; // "XPR2" - Xbox Packed Resource
        byte[] xpr0Signature = { 0x58, 0x50, 0x52, 0x30 }; // "XPR0"
        byte[] ddsSignature = { 0x44, 0x44, 0x53, 0x20 }; // "DDS "

        // Read entire file into memory for scanning (limit to 50MB for safety)
        _fileStream.Seek(0, SeekOrigin.Begin);
        int fileSize = (int)Math.Min(_fileStream.Length, 50 * 1024 * 1024);
        byte[] buffer = _reader.ReadBytes(fileSize);

        int imageCount = 0;

        // Scan for XPR2 textures (Xbox 360 packed resources)
        Log($"    Scanning {fileSize} bytes for XPR2 textures...");
        for (int i = 0; i <= buffer.Length - 12; i++)
        {
            if (MatchesSignature(buffer, i, xpr2Signature))
            {
                // XPR2 header: magic (4) + size (4) + header size (4) + ...
                uint totalSize = BitConverter.ToUInt32(buffer, i + 4);
                uint headerSize = BitConverter.ToUInt32(buffer, i + 8);

                // Sanity check
                if (totalSize > 0 && totalSize < 10 * 1024 * 1024 && totalSize <= buffer.Length - i)
                {
                    byte[] xprData = new byte[totalSize];
                    Array.Copy(buffer, i, xprData, 0, (int)totalSize);

                    // Try to convert XPR2 to PNG
                    byte[]? pngData = ConvertXpr2ToPng(xprData);
                    if (pngData != null)
                    {
                        resources.Add(new XexResource
                        {
                            Name = $"icon_{imageCount++}.png",
                            Offset = (uint)i,
                            Size = (uint)pngData.Length,
                            Data = pngData,
                            Type = "PNG"
                        });
                        Log($"    Found XPR2 texture at offset 0x{i:X}, converted to PNG ({pngData.Length} bytes)");
                    }
                    else
                    {
                        // Store raw XPR2 if conversion failed
                        resources.Add(new XexResource
                        {
                            Name = $"texture_{imageCount++}.xpr",
                            Offset = (uint)i,
                            Size = totalSize,
                            Data = xprData,
                            Type = "XPR2"
                        });
                        Log($"    Found XPR2 texture at offset 0x{i:X}, size {totalSize} bytes (raw)");
                    }
                    i += (int)totalSize - 1;
                }
            }
        }

        // Scan for XPR0 textures
        for (int i = 0; i <= buffer.Length - 12; i++)
        {
            if (MatchesSignature(buffer, i, xpr0Signature))
            {
                uint totalSize = BitConverter.ToUInt32(buffer, i + 4);
                if (totalSize > 0 && totalSize < 10 * 1024 * 1024 && totalSize <= buffer.Length - i)
                {
                    byte[] xprData = new byte[totalSize];
                    Array.Copy(buffer, i, xprData, 0, (int)totalSize);

                    resources.Add(new XexResource
                    {
                        Name = $"texture_{imageCount++}.xpr",
                        Offset = (uint)i,
                        Size = totalSize,
                        Data = xprData,
                        Type = "XPR0"
                    });
                    Log($"    Found XPR0 texture at offset 0x{i:X}, size {totalSize} bytes");
                    i += (int)totalSize - 1;
                }
            }
        }

        // Scan for DDS textures
        for (int i = 0; i <= buffer.Length - 128; i++)
        {
            if (MatchesSignature(buffer, i, ddsSignature))
            {
                // DDS header size is at offset 4, should be 124
                uint headerSize = BitConverter.ToUInt32(buffer, i + 4);
                if (headerSize == 124)
                {
                    // Get image dimensions and calculate size
                    uint height = BitConverter.ToUInt32(buffer, i + 12);
                    uint width = BitConverter.ToUInt32(buffer, i + 16);
                    uint pitchOrLinearSize = BitConverter.ToUInt32(buffer, i + 20);

                    if (width > 0 && width <= 4096 && height > 0 && height <= 4096)
                    {
                        // Estimate DDS size (header + data)
                        uint ddsSize = 128 + pitchOrLinearSize;
                        if (ddsSize > 0 && ddsSize <= buffer.Length - i)
                        {
                            byte[] ddsData = new byte[ddsSize];
                            Array.Copy(buffer, i, ddsData, 0, (int)ddsSize);

                            resources.Add(new XexResource
                            {
                                Name = $"texture_{imageCount++}.dds",
                                Offset = (uint)i,
                                Size = ddsSize,
                                Data = ddsData,
                                Type = "DDS"
                            });
                            Log($"    Found DDS texture at offset 0x{i:X}, {width}x{height}");
                            i += (int)ddsSize - 1;
                        }
                    }
                }
            }
        }

        // Scan for PNG images
        Log($"    Scanning for PNG signatures...");
        for (int i = 0; i <= buffer.Length - pngSignature.Length; i++)
        {
            if (MatchesSignature(buffer, i, pngSignature))
            {
                int endPos = FindPngEnd(buffer, i);
                if (endPos > i)
                {
                    int imgSize = endPos - i;
                    byte[] imgData = new byte[imgSize];
                    Array.Copy(buffer, i, imgData, 0, imgSize);

                    resources.Add(new XexResource
                    {
                        Name = $"icon_{imageCount++}.png",
                        Offset = (uint)i,
                        Size = (uint)imgSize,
                        Data = imgData,
                        Type = "PNG"
                    });

                    Log($"    Found PNG at offset 0x{i:X}, size {imgSize} bytes");
                    i = endPos - 1;
                }
            }
        }

        // Scan for JPEG images
        for (int i = 0; i <= buffer.Length - jpegSignature.Length; i++)
        {
            if (MatchesSignature(buffer, i, jpegSignature))
            {
                int endPos = FindJpegEnd(buffer, i);
                if (endPos > i && endPos - i > 100)
                {
                    int imgSize = endPos - i;
                    byte[] imgData = new byte[imgSize];
                    Array.Copy(buffer, i, imgData, 0, imgSize);

                    // Validate the JPEG structure
                    if (IsValidJpeg(imgData))
                    {
                        resources.Add(new XexResource
                        {
                            Name = $"image_{imageCount++}.jpg",
                            Offset = (uint)i,
                            Size = (uint)imgSize,
                            Data = imgData,
                            Type = "JPEG"
                        });

                        Log($"    Found JPEG at offset 0x{i:X}, size {imgSize} bytes");
                        i = endPos - 1;
                    }
                    else
                    {
                        Log($"    Skipping invalid JPEG at offset 0x{i:X}");
                    }
                }
            }
        }

        if (resources.Count == 0)
        {
            Log($"    No images found in file");
        }
        else
        {
            Log($"    Found {resources.Count} image(s) total");
        }
    }

    /// <summary>
    /// Convert XPR2 texture to PNG format
    /// </summary>
    private byte[]? ConvertXpr2ToPng(byte[] xprData)
    {
        try
        {
            if (xprData.Length < 24) return null;

            // XPR2 format:
            // 0x00: "XPR2" magic
            // 0x04: total size
            // 0x08: header size
            // 0x0C: texture count
            // Then texture headers follow

            uint headerSize = BitConverter.ToUInt32(xprData, 8);
            uint textureCount = BitConverter.ToUInt32(xprData, 12);

            if (textureCount == 0 || headerSize < 20) return null;

            // Read first texture header (simplified - assumes first texture)
            // The texture data starts after the header
            int dataOffset = (int)headerSize;
            if (dataOffset >= xprData.Length) return null;

            // Try to determine texture format from header
            // XPR2 textures are typically DXT compressed
            // For now, extract raw texture data for manual inspection

            int dataSize = xprData.Length - dataOffset;
            if (dataSize <= 0) return null;

            // Check if it looks like a standard image format after header
            if (dataOffset + 8 < xprData.Length)
            {
                // Check for PNG magic after header
                if (xprData[dataOffset] == 0x89 && xprData[dataOffset + 1] == 0x50 &&
                    xprData[dataOffset + 2] == 0x4E && xprData[dataOffset + 3] == 0x47)
                {
                    // It's a PNG inside XPR2
                    int pngEnd = FindPngEndInArray(xprData, dataOffset);
                    if (pngEnd > dataOffset)
                    {
                        int pngSize = pngEnd - dataOffset;
                        byte[] pngData = new byte[pngSize];
                        Array.Copy(xprData, dataOffset, pngData, 0, pngSize);
                        return pngData;
                    }
                }
            }

            // If not a standard format, return null (can't convert)
            return null;
        }
        catch
        {
            return null;
        }
    }

    private int FindPngEndInArray(byte[] buffer, int start)
    {
        byte[] iend = { 0x49, 0x45, 0x4E, 0x44 };
        for (int i = start + 8; i <= buffer.Length - 8; i++)
        {
            if (MatchesSignature(buffer, i, iend))
            {
                return i + 8;
            }
        }
        return -1;
    }

    private static bool MatchesSignature(byte[] buffer, int offset, byte[] signature)
    {
        if (offset + signature.Length > buffer.Length) return false;
        for (int i = 0; i < signature.Length; i++)
        {
            if (buffer[offset + i] != signature[i]) return false;
        }
        return true;
    }

    private static int FindPngEnd(byte[] buffer, int start)
    {
        // PNG ends with IEND chunk: 00 00 00 00 49 45 4E 44 AE 42 60 82
        byte[] iend = { 0x49, 0x45, 0x4E, 0x44 }; // "IEND"

        for (int i = start + 8; i <= buffer.Length - 8; i++)
        {
            if (MatchesSignature(buffer, i, iend))
            {
                return i + 8; // Include the CRC after IEND
            }
        }
        return -1;
    }

    private static int FindJpegEnd(byte[] buffer, int start)
    {
        // Validate JPEG structure and find proper end
        // JPEG format: FF D8 FF [markers with lengths] ... FF D9

        if (start + 10 > buffer.Length) return -1;

        // Must start with FFD8FF
        if (buffer[start] != 0xFF || buffer[start + 1] != 0xD8 || buffer[start + 2] != 0xFF)
            return -1;

        int pos = start + 2;
        bool foundSOS = false;

        // Parse JPEG markers
        while (pos < buffer.Length - 1)
        {
            if (buffer[pos] != 0xFF)
            {
                if (foundSOS)
                {
                    // After SOS, scan for EOI marker
                    pos++;
                    continue;
                }
                return -1; // Invalid JPEG structure
            }

            // Skip FF padding bytes
            while (pos < buffer.Length - 1 && buffer[pos] == 0xFF && buffer[pos + 1] == 0xFF)
                pos++;

            if (pos >= buffer.Length - 1) return -1;

            byte marker = buffer[pos + 1];
            pos += 2;

            // End of image
            if (marker == 0xD9)
            {
                return pos;
            }

            // Start of scan (SOS) - after this comes compressed data until EOI
            if (marker == 0xDA)
            {
                foundSOS = true;
                // Skip SOS header
                if (pos + 2 > buffer.Length) return -1;
                int sosLen = (buffer[pos] << 8) | buffer[pos + 1];
                pos += sosLen;

                // Now scan for EOI (FF D9) in the compressed data
                while (pos < buffer.Length - 1)
                {
                    if (buffer[pos] == 0xFF && buffer[pos + 1] == 0xD9)
                    {
                        return pos + 2;
                    }
                    pos++;
                }
                return -1;
            }

            // Restart markers (RST0-RST7) and standalone markers have no length
            if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                continue;
            }

            // All other markers have a 2-byte length field
            if (pos + 2 > buffer.Length) return -1;
            int segLen = (buffer[pos] << 8) | buffer[pos + 1];
            if (segLen < 2) return -1; // Invalid segment length
            pos += segLen;
        }

        return -1;
    }

    /// <summary>
    /// Validate that a byte array contains a valid JPEG
    /// </summary>
    private static bool IsValidJpeg(byte[] data)
    {
        if (data == null || data.Length < 10) return false;

        // Check SOI marker
        if (data[0] != 0xFF || data[1] != 0xD8) return false;

        // Check EOI marker at end
        if (data[data.Length - 2] != 0xFF || data[data.Length - 1] != 0xD9) return false;

        // Check for at least one valid segment marker (usually APP0 or APP1)
        if (data[2] != 0xFF) return false;
        byte firstMarker = data[3];

        // Common valid markers after SOI: APP0-APP15 (E0-EF), DQT (DB), SOF (C0-C3)
        bool validFirstMarker = (firstMarker >= 0xE0 && firstMarker <= 0xEF) || // APPn
                                 firstMarker == 0xDB || // DQT
                                 (firstMarker >= 0xC0 && firstMarker <= 0xC3); // SOFn

        return validFirstMarker;
    }

    private static string DetectImageType(byte[] data)
    {
        if (data == null || data.Length < 8) return "Unknown";

        // PNG
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "PNG";

        // JPEG
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "JPEG";

        // DDS
        if (data.Length >= 4 && data[0] == 0x44 && data[1] == 0x44 && data[2] == 0x53 && data[3] == 0x20)
            return "DDS";

        // BMP
        if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
            return "BMP";

        // GIF
        if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            return "GIF";

        return "Unknown";
    }

    private ExecutionId? DecodeExecutionId(uint dataOffset)
    {
        _fileStream.Seek(dataOffset, SeekOrigin.Begin);

        var execId = new ExecutionId
        {
            MediaId = ReadUInt32BigEndian(),
            Version = ReadUInt32BigEndian(),
            BaseVersion = ReadUInt32BigEndian(),
            TitleId = ReadUInt32BigEndian(),
            Platform = _reader.ReadByte(),
            ExecutableType = _reader.ReadByte(),
            DiscNum = _reader.ReadByte(),
            DiscsInSet = _reader.ReadByte(),
            SaveGameId = ReadUInt32BigEndian()
        };

        Log($"    Media ID: 0x{execId.MediaId:X8}");
        Log($"    Title ID: 0x{execId.TitleId:X8}");
        Log($"    Version: {execId.Version >> 16}.{execId.Version & 0xFFFF}");
        Log($"    Platform: {execId.Platform}");
        Log($"    Disc: {execId.DiscNum}/{execId.DiscsInSet}");

        return execId;
    }

    private CompressionInfo? DecodeCompression(uint dataOffset)
    {
        _fileStream.Seek(dataOffset, SeekOrigin.Begin);
        uint infoSize = ReadUInt32BigEndian();

        Log($"    Structure size: 0x{infoSize:X} ({infoSize} bytes)");

        if (infoSize < 4)
        {
            Log($"    Invalid compression info size");
            return null;
        }

        // Read the raw data for parsing
        byte[] rawData = _reader.ReadBytes((int)infoSize);

        var compInfo = new CompressionInfo
        {
            InfoSize = infoSize,
            RawData = rawData
        };

        // Parse encryption type (first 2 bytes, big-endian)
        if (rawData.Length >= 2)
        {
            compInfo.EncryptionType = (XeEncryptionType)((rawData[0] << 8) | rawData[1]);
            Log($"    Encryption type: {compInfo.EncryptionType} ({(int)compInfo.EncryptionType})");
        }

        // Parse compression type (bytes 2-3, big-endian)
        if (rawData.Length >= 4)
        {
            compInfo.CompressionType = (XeCompressionType)((rawData[2] << 8) | rawData[3]);
            Log($"    Compression type: {compInfo.CompressionType} ({(int)compInfo.CompressionType})");
        }

        // Parse based on compression type
        switch (compInfo.CompressionType)
        {
            case XeCompressionType.Raw:
                // Raw format: data blocks follow (data_size, zero_size pairs)
                Log($"    Format: Raw/Uncompressed (encrypted={compInfo.EncryptionType == XeEncryptionType.Encrypted})");
                if (rawData.Length >= 8)
                {
                    // First block info
                    uint dataSize = (uint)((rawData[4] << 24) | (rawData[5] << 16) | (rawData[6] << 8) | rawData[7]);
                    Log($"    First data block size: 0x{dataSize:X}");
                }
                break;

            case XeCompressionType.Compressed:
                // LZX compressed format: compression_window (4), then block info
                if (rawData.Length >= 8)
                {
                    compInfo.CompressionWindow = (uint)((rawData[4] << 24) | (rawData[5] << 16) | (rawData[6] << 8) | rawData[7]);
                    Log($"    Compression window: 0x{compInfo.CompressionWindow:X}");
                }
                if (rawData.Length >= 12)
                {
                    compInfo.BlockSize = (uint)((rawData[8] << 24) | (rawData[9] << 16) | (rawData[10] << 8) | rawData[11]);
                    Log($"    First block size: 0x{compInfo.BlockSize:X}");
                }
                if (rawData.Length >= 32)
                {
                    Array.Copy(rawData, 12, compInfo.Hash, 0, 20);
                    Log($"    Block hash: {BitConverter.ToString(compInfo.Hash).Replace("-", " ")}");
                }
                break;

            case XeCompressionType.Zeroed:
                Log($"    Format: Zeroed (no data)");
                break;

            case XeCompressionType.DeltaCompressed:
                Log($"    Format: Delta compressed");
                break;

            default:
                Log($"    Unknown compression type: {(int)compInfo.CompressionType}");
                Log($"    Raw data: {BitConverter.ToString(rawData).Replace("-", " ")}");
                break;
        }

        return compInfo;
    }

    private byte[] DecryptSessionKey(byte[] encryptedKey)
    {
        // Use null key for decryption (retail key)
        byte[] nullKey = new byte[16];

        using var aes = Aes.Create();
        aes.Key = nullKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        byte[] decrypted = new byte[16];
        decryptor.TransformBlock(encryptedKey, 0, 16, decrypted, 0);

        return decrypted;
    }

    /// <summary>
    /// AES CBC decryption
    /// </summary>
    private void AesDecryptCbc(byte[] key, byte[] buffer, int length)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();

        for (int i = 0; i < length; i += 16)
        {
            byte[] temp = new byte[16];
            Array.Copy(buffer, i, temp, 0, 16);

            byte[] decrypted = new byte[16];
            decryptor.TransformBlock(buffer, i, 16, decrypted, 0);

            // XOR with last ciphertext block
            for (int j = 0; j < 16; j++)
            {
                buffer[i + j] = (byte)(decrypted[j] ^ _lastCipherTextBlock[j]);
            }

            Array.Copy(temp, _lastCipherTextBlock, 16);
        }
    }

    /// <summary>
    /// Extract the PE file from a compressed XEX
    /// </summary>
    public void ExtractPE(string outputPath, XexFileInfo info, Action<string> logCallback)
    {
        if (info.SessionKey == null)
        {
            throw new InvalidOperationException("No session key found");
        }

        // Reset CBC state
        _lastCipherTextBlock = new byte[16];

        _fileStream.Seek(info.DataOffset, SeekOrigin.Begin);

        if (info.Compression == null)
        {
            // Uncompressed XEX - just decrypt and copy
            ExtractUncompressed(outputPath, info, logCallback);
            return;
        }

        var compression = info.Compression;
        logCallback($"Compression type: {compression.CompressionType}");
        logCallback($"Encryption type: {compression.EncryptionType}");

        // Handle based on compression type
        switch (compression.CompressionType)
        {
            case XeCompressionType.Raw:
                // Raw format - decrypt only, no LZX decompression
                ExtractRawEncrypted(outputPath, info, logCallback);
                break;

            case XeCompressionType.Compressed:
                // LZX compressed - decrypt and decompress
                ExtractLzxCompressed(outputPath, info, logCallback);
                break;

            case XeCompressionType.Zeroed:
                logCallback("Zeroed compression type - no data to extract");
                break;

            case XeCompressionType.DeltaCompressed:
                logCallback("Delta compression is not supported yet");
                break;

            default:
                logCallback($"Unknown compression type: {compression.CompressionType}");
                // Try raw extraction as fallback
                ExtractUncompressed(outputPath, info, logCallback);
                break;
        }
    }

    /// <summary>
    /// Extract raw encrypted PE data (no LZX compression)
    /// </summary>
    private void ExtractRawEncrypted(string outputPath, XexFileInfo info, Action<string> logCallback)
    {
        logCallback("Extracting raw encrypted XEX data...");

        _fileStream.Seek(info.DataOffset, SeekOrigin.Begin);

        // For raw format, the data is just encrypted but not LZX compressed
        // We need to decrypt block by block
        long dataSize = _fileStream.Length - info.DataOffset;
        if (info.ImageSize > 0 && info.ImageSize < dataSize)
        {
            dataSize = info.ImageSize;
        }

        using var outputFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

        // Read and decrypt in chunks
        byte[] buffer = new byte[0x10000]; // 64KB chunks
        long remaining = dataSize;
        long totalWritten = 0;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = _reader.Read(buffer, 0, toRead);
            if (read == 0) break;

            // Decrypt if encrypted
            if (info.Compression?.EncryptionType == XeEncryptionType.Encrypted && info.SessionKey != null && read >= 16)
            {
                AesDecryptCbc(info.SessionKey, buffer, (read / 16) * 16);
            }

            outputFile.Write(buffer, 0, read);
            remaining -= read;
            totalWritten += read;

            if (totalWritten % 0x100000 == 0) // Log every 1MB
            {
                logCallback($".");
            }
        }

        logCallback($"\nExtraction complete! Output written to: {outputPath} ({totalWritten} bytes)");
    }

    /// <summary>
    /// Extract LZX compressed PE data
    /// </summary>
    private void ExtractLzxCompressed(string outputPath, XexFileInfo info, Action<string> logCallback)
    {
        var compression = info.Compression!;

        // Check if we have hash verification
        bool hasHashVerification = compression.Hash.Any(b => b != 0);

        string compressedDataPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", "compressed_data.tmp");

        using (var compressedFile = new FileStream(compressedDataPath, FileMode.Create, FileAccess.Write))
        {
            uint blockSize = compression.BlockSize;
            byte[] blockHash = new byte[20];
            if (hasHashVerification)
            {
                Array.Copy(compression.Hash, blockHash, 20);
            }

            // The format is multiple blocks:
            // |SIZE(next block) HASH(next block) DATA |
            // The first size, hash is given in the compressionInfo in the file header
            // Every block contains the size & hash of the NEXT block

            while (blockSize > 0)
            {
                byte[] buf = _reader.ReadBytes((int)blockSize);

                // Decrypt if encrypted
                if (compression.EncryptionType == XeEncryptionType.Encrypted && info.SessionKey != null)
                {
                    AesDecryptCbc(info.SessionKey, buf, (int)blockSize);
                }

                // Verify SHA1 hash (only if we have hash verification)
                if (hasHashVerification)
                {
                    byte[] calculatedHash;
                    using (var sha1 = SHA1.Create())
                    {
                        calculatedHash = sha1.ComputeHash(buf);
                    }

                    if (!calculatedHash.SequenceEqual(blockHash))
                    {
                        logCallback("\nWARNING: compressed data hash mismatch - data may be corrupt!");
                        // Continue anyway for debugging purposes
                    }
                }

                logCallback($"\ncompressed block with size {blockSize} -> ");

                // Get next block info from decrypted data
                blockSize = SwapEndian(buf, 0);
                if (hasHashVerification)
                {
                    Array.Copy(buf, 4, blockHash, 0, 20);
                }

                // Write LZX data chunks
                int offset = hasHashVerification ? 24 : 4; // Skip size (4) + hash (20) or just size (4)
                int totalWritten = 0;

                while (offset < buf.Length)
                {
                    if (offset + 2 > buf.Length) break;

                    ushort chunkLen = (ushort)((buf[offset] << 8) | buf[offset + 1]);
                    if (chunkLen == 0) break;

                    offset += 2;
                    if (offset + chunkLen > buf.Length) break;

                    compressedFile.Write(buf, offset, chunkLen);
                    totalWritten += chunkLen;
                    offset += chunkLen;
                }

                logCallback($"{totalWritten}");
            }
        }

        // Decompress LZX data
        logCallback("\nDecompressing LZX data...");

        try
        {
            using var compressedInput = new FileStream(compressedDataPath, FileMode.Open, FileAccess.Read);
            using var outputFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

            // Get window bits from compression window value
            int windowBits = GetWindowBits(compression.CompressionWindow);
            logCallback($"\nUsing LZX window bits: {windowBits}");

            var decompressor = new LzxDecompressor(windowBits);
            decompressor.Decompress(compressedInput, outputFile, logCallback);

            logCallback($"\nDecompression complete! Output written to: {outputPath}");
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(compressedDataPath))
            {
                File.Delete(compressedDataPath);
            }
        }
    }

    /// <summary>
    /// Extract uncompressed XEX data
    /// </summary>
    private void ExtractUncompressed(string outputPath, XexFileInfo info, Action<string> logCallback)
    {
        logCallback("Extracting uncompressed XEX data...");

        _fileStream.Seek(info.DataOffset, SeekOrigin.Begin);

        // Calculate size to read (from data offset to end of file, or image size)
        long dataSize = _fileStream.Length - info.DataOffset;
        if (info.ImageSize > 0 && info.ImageSize < dataSize)
        {
            dataSize = info.ImageSize;
        }

        using var outputFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

        // Read and decrypt in chunks
        byte[] buffer = new byte[4096];
        long remaining = dataSize;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = _reader.Read(buffer, 0, toRead);
            if (read == 0) break;

            // Decrypt if we have a session key
            if (info.SessionKey != null && read >= 16)
            {
                AesDecryptCbc(info.SessionKey, buffer, (read / 16) * 16);
            }

            outputFile.Write(buffer, 0, read);
            remaining -= read;
        }

        logCallback($"\nExtraction complete! Output written to: {outputPath}");
    }

    /// <summary>
    /// Get LZX window bits from compression window value
    /// </summary>
    private static int GetWindowBits(uint compressionWindow)
    {
        // Compression window is typically stored as a power of 2
        // Common values: 0x8000 (32KB = 15 bits), 0x10000 (64KB = 16 bits), etc.
        if (compressionWindow == 0) return 15; // Default

        int bits = 0;
        uint value = compressionWindow;
        while (value > 1)
        {
            value >>= 1;
            bits++;
        }

        // Clamp to valid LZX range (15-21)
        return Math.Clamp(bits, 15, 21);
    }

    /// <summary>
    /// Extract resources from an extracted PE file
    /// </summary>
    /// <param name="peFilePath">Path to the extracted PE file</param>
    /// <param name="info">XexFileInfo containing resource metadata</param>
    /// <param name="logCallback">Callback for logging</param>
    /// <returns>List of extracted resources with data</returns>
    public static List<XexResource> ExtractResourcesFromPE(string peFilePath, XexFileInfo info, Action<string>? logCallback = null)
    {
        var extractedResources = new List<XexResource>();

        if (!File.Exists(peFilePath))
        {
            logCallback?.Invoke($"PE file not found: {peFilePath}");
            return extractedResources;
        }

        using var fs = new FileStream(peFilePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        byte[] peData = reader.ReadBytes((int)fs.Length);

        foreach (var res in info.Resources.Where(r => r.Type == "PE_EMBEDDED"))
        {
            // Calculate offset in PE: resource_address - image_base_address
            if (info.ImageBaseAddress > 0 && res.Offset >= info.ImageBaseAddress)
            {
                long peOffset = res.Offset - info.ImageBaseAddress;

                if (peOffset >= 0 && peOffset + res.Size <= peData.Length)
                {
                    byte[] resourceData = new byte[res.Size];
                    Array.Copy(peData, peOffset, resourceData, 0, (int)res.Size);

                    // Detect the actual type
                    string type = DetectImageType(resourceData);

                    extractedResources.Add(new XexResource
                    {
                        Name = res.Name,
                        Offset = res.Offset,
                        Size = res.Size,
                        Data = resourceData,
                        Type = type
                    });

                    logCallback?.Invoke($"Extracted resource '{res.Name}' at PE offset 0x{peOffset:X}, {resourceData.Length} bytes, type: {type}");
                }
                else
                {
                    logCallback?.Invoke($"Resource '{res.Name}' at offset 0x{peOffset:X} is outside PE bounds (PE size: 0x{peData.Length:X})");
                }
            }
        }

        // Also scan the PE for any images that weren't in the resource table
        logCallback?.Invoke("Scanning extracted PE for additional images...");

        // PNG signature
        byte[] pngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i <= peData.Length - pngSig.Length; i++)
        {
            if (MatchesSignatureStatic(peData, i, pngSig))
            {
                int endPos = FindPngEndStatic(peData, i);
                if (endPos > i)
                {
                    int size = endPos - i;
                    byte[] imgData = new byte[size];
                    Array.Copy(peData, i, imgData, 0, size);

                    extractedResources.Add(new XexResource
                    {
                        Name = $"pe_icon_{extractedResources.Count}.png",
                        Offset = (uint)i,
                        Size = (uint)size,
                        Data = imgData,
                        Type = "PNG"
                    });
                    logCallback?.Invoke($"Found PNG in PE at offset 0x{i:X}, size {size} bytes");
                    i = endPos - 1;
                }
            }
        }

        return extractedResources;
    }

    private static bool MatchesSignatureStatic(byte[] buffer, int offset, byte[] signature)
    {
        if (offset + signature.Length > buffer.Length) return false;
        for (int i = 0; i < signature.Length; i++)
        {
            if (buffer[offset + i] != signature[i]) return false;
        }
        return true;
    }

    private static int FindPngEndStatic(byte[] buffer, int start)
    {
        byte[] iend = { 0x49, 0x45, 0x4E, 0x44 };
        for (int i = start + 8; i <= buffer.Length - 8; i++)
        {
            if (MatchesSignatureStatic(buffer, i, iend))
            {
                return i + 8;
            }
        }
        return -1;
    }
}
