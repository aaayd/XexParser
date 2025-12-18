using System.Text;

namespace XexTool.Xex;

/// <summary>
/// Parser for Xbox Dashboard File (XDBF) format
/// Used to extract game titles and other metadata from XEX resources
/// </summary>
public static class XdbfParser
{
    // XDBF magic number
    private static readonly byte[] XdbfMagic = { 0x58, 0x44, 0x42, 0x46 }; // "XDBF"

    // Namespace IDs
    private const ushort XDBF_SPA_NAMESPACE = 1;  // String namespace
    private const ushort XDBF_IMAGE_NAMESPACE = 2; // Image namespace
    private const ushort XDBF_SETTING_NAMESPACE = 3; // Setting namespace

    // Known resource IDs for strings
    private const ulong XDBF_XSTC = 0x58535443; // "XSTC" - String table config
    private const ulong XDBF_XTHD = 0x58544844; // "XTHD" - Title header
    private const ulong XDBF_TITLE_ID = 0x8000; // Title string ID (English)

    /// <summary>
    /// Extract game title from XDBF data
    /// </summary>
    public static string? ExtractGameTitle(byte[] xdbfData)
    {
        if (xdbfData == null || xdbfData.Length < 24)
            return null;

        // Check for XDBF magic
        if (!HasXdbfMagic(xdbfData, 0))
            return null;

        try
        {
            // Parse XDBF header
            // Offset 0: Magic (4 bytes)
            // Offset 4: Version (4 bytes)
            // Offset 8: Entry table length (4 bytes)
            // Offset 12: Entry count (4 bytes)
            // Offset 16: Free table length (4 bytes)
            // Offset 20: Free count (4 bytes)

            int entryTableLength = ReadInt32BE(xdbfData, 8);
            int entryCount = ReadInt32BE(xdbfData, 12);
            int freeTableLength = ReadInt32BE(xdbfData, 16);

            // Entry table starts at offset 24
            int entryTableOffset = 24;
            int dataOffset = entryTableOffset + (entryCount * 18) + (freeTableLength * 8);

            // Look for string entries (namespace 1)
            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = entryTableOffset + (i * 18);
                if (entryOffset + 18 > xdbfData.Length) break;

                ushort namespaceId = ReadUInt16BE(xdbfData, entryOffset);
                ulong resourceId = ReadUInt64BE(xdbfData, entryOffset + 2);
                int offsetSpec = ReadInt32BE(xdbfData, entryOffset + 10);
                int length = ReadInt32BE(xdbfData, entryOffset + 14);

                // Look for title string (resource ID 0x8000 in string namespace)
                if (namespaceId == XDBF_SPA_NAMESPACE && resourceId == XDBF_TITLE_ID)
                {
                    int stringOffset = dataOffset + offsetSpec;
                    if (stringOffset >= 0 && stringOffset + length <= xdbfData.Length && length > 0)
                    {
                        // String is Unicode (UTF-16 BE)
                        string title = ReadUnicodeStringBE(xdbfData, stringOffset, length);
                        if (!string.IsNullOrWhiteSpace(title))
                            return title;
                    }
                }
            }

            // Try alternate approach - scan for Unicode strings after XSTC marker
            return ScanForTitle(xdbfData);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scan XDBF data for game title using string table
    /// </summary>
    private static string? ScanForTitle(byte[] data)
    {
        // Look for XSTC (string table config) marker
        byte[] xstcMarker = { 0x58, 0x53, 0x54, 0x43 }; // "XSTC"

        for (int i = 0; i < data.Length - 100; i++)
        {
            if (MatchesBytes(data, i, xstcMarker))
            {
                // XSTC found, string table follows
                // Format: XSTC (4) + version (4) + size (4) + default language (4) + string count (4)
                if (i + 20 > data.Length) continue;

                int stringCount = ReadInt32BE(data, i + 16);
                if (stringCount <= 0 || stringCount > 100) continue;

                // String entries follow: id (4) + offset (4) for each
                int stringsStart = i + 20 + (stringCount * 8);

                // Look for the first non-empty string (usually the title)
                for (int j = 0; j < stringCount && j < 5; j++)
                {
                    int entryOffset = i + 20 + (j * 8);
                    if (entryOffset + 8 > data.Length) break;

                    int strOffset = ReadInt32BE(data, entryOffset + 4);
                    int actualOffset = stringsStart + strOffset;

                    if (actualOffset >= 0 && actualOffset < data.Length - 2)
                    {
                        string str = ReadUnicodeStringBE(data, actualOffset, Math.Min(256, data.Length - actualOffset));
                        if (!string.IsNullOrWhiteSpace(str) && str.Length > 2)
                            return str;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Try to extract game title from raw XEX/PE data by scanning for Unicode strings
    /// </summary>
    public static string? ScanForGameTitle(byte[] data, uint titleId)
    {
        if (data == null || data.Length < 100)
            return null;

        // First, try to find XDBF section
        for (int i = 0; i < data.Length - 24; i++)
        {
            if (HasXdbfMagic(data, i))
            {
                // Found XDBF, try to parse it
                int remainingLength = data.Length - i;
                byte[] xdbfData = new byte[Math.Min(remainingLength, 1024 * 1024)]; // Max 1MB
                Array.Copy(data, i, xdbfData, 0, xdbfData.Length);

                string? title = ExtractGameTitle(xdbfData);
                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }
        }

        return null;
    }

    private static bool HasXdbfMagic(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return false;
        return data[offset] == 'X' && data[offset + 1] == 'D' &&
               data[offset + 2] == 'B' && data[offset + 3] == 'F';
    }

    private static bool MatchesBytes(byte[] data, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > data.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (data[offset + i] != pattern[i]) return false;
        }
        return true;
    }

    private static ushort ReadUInt16BE(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static int ReadInt32BE(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3];
    }

    private static ulong ReadUInt64BE(byte[] data, int offset)
    {
        return ((ulong)data[offset] << 56) | ((ulong)data[offset + 1] << 48) |
               ((ulong)data[offset + 2] << 40) | ((ulong)data[offset + 3] << 32) |
               ((ulong)data[offset + 4] << 24) | ((ulong)data[offset + 5] << 16) |
               ((ulong)data[offset + 6] << 8) | data[offset + 7];
    }

    private static string ReadUnicodeStringBE(byte[] data, int offset, int maxLength)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < maxLength - 1; i += 2)
        {
            if (offset + i + 1 >= data.Length) break;

            // Big-endian UTF-16
            char c = (char)((data[offset + i] << 8) | data[offset + i + 1]);
            if (c == '\0') break;
            if (c >= 32 && c < 0xFFFE) // Valid printable character
                sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
