using System.IO;

namespace XexTool.Xex;

/// <summary>
/// LZX decompressor - ported from libmspack
/// Original (C) 2003-2004 Stuart Caie, LGPL 2.1
/// The LZX method was created by Jonathan Forbes and Tomi Poutanen, adapted by Microsoft Corporation.
/// </summary>
public class LzxDecompressor
{
    // LZX constants
    private const int LZX_MIN_MATCH = 2;
    private const int LZX_MAX_MATCH = 257;
    private const int LZX_NUM_CHARS = 256;
    private const int LZX_BLOCKTYPE_INVALID = 0;
    private const int LZX_BLOCKTYPE_VERBATIM = 1;
    private const int LZX_BLOCKTYPE_ALIGNED = 2;
    private const int LZX_BLOCKTYPE_UNCOMPRESSED = 3;
    private const int LZX_PRETREE_NUM_ELEMENTS = 20;
    private const int LZX_ALIGNED_NUM_ELEMENTS = 8;
    private const int LZX_NUM_PRIMARY_LENGTHS = 7;
    private const int LZX_NUM_SECONDARY_LENGTHS = 249;

    // Huffman table sizes
    private const int LZX_PRETREE_MAXSYMBOLS = LZX_PRETREE_NUM_ELEMENTS;
    private const int LZX_PRETREE_TABLEBITS = 6;
    private const int LZX_MAINTREE_MAXSYMBOLS = LZX_NUM_CHARS + 50 * 8;
    private const int LZX_MAINTREE_TABLEBITS = 12;
    private const int LZX_LENGTH_MAXSYMBOLS = LZX_NUM_SECONDARY_LENGTHS + 1;
    private const int LZX_LENGTH_TABLEBITS = 12;
    private const int LZX_ALIGNED_MAXSYMBOLS = LZX_ALIGNED_NUM_ELEMENTS;
    private const int LZX_ALIGNED_TABLEBITS = 7;
    private const int LZX_LENTABLE_SAFETY = 64;
    private const int LZX_FRAME_SIZE = 32768;

    // Static lookup tables
    private static readonly uint[] PositionBase = new uint[51];
    private static readonly byte[] ExtraBits = new byte[51];
    private static bool _staticInitialized = false;

    // Instance state
    private readonly int _windowBits;
    private readonly uint _windowSize;
    private readonly int _posnSlots;
    private byte[] _window = null!;
    private uint _windowPosn;
    private uint _framePosn;
    private uint _frame;
    private uint _resetInterval;

    private uint _R0, _R1, _R2;
    private uint _blockLength;
    private uint _blockRemaining;
    private int _blockType;
    private bool _headerRead;
    private int _intelFilesize;
    private int _intelCurpos;
    private bool _intelStarted;

    // Bit buffer
    private uint _bitBuffer;
    private int _bitsLeft;

    // Input buffer
    private byte[] _inbuf = null!;
    private int _iPtr;
    private int _iEnd;
    private bool _inputEnd;

    // Huffman tables
    private readonly byte[] _pretreeLen = new byte[LZX_PRETREE_MAXSYMBOLS + LZX_LENTABLE_SAFETY];
    private readonly byte[] _maintreeLen = new byte[LZX_MAINTREE_MAXSYMBOLS + LZX_LENTABLE_SAFETY];
    private readonly byte[] _lengthLen = new byte[LZX_LENGTH_MAXSYMBOLS + LZX_LENTABLE_SAFETY];
    private readonly byte[] _alignedLen = new byte[LZX_ALIGNED_MAXSYMBOLS + LZX_LENTABLE_SAFETY];

    private readonly ushort[] _pretreeTable = new ushort[(1 << LZX_PRETREE_TABLEBITS) + (LZX_PRETREE_MAXSYMBOLS * 2)];
    private readonly ushort[] _maintreeTable = new ushort[(1 << LZX_MAINTREE_TABLEBITS) + (LZX_MAINTREE_MAXSYMBOLS * 2)];
    private readonly ushort[] _lengthTable = new ushort[(1 << LZX_LENGTH_TABLEBITS) + (LZX_LENGTH_MAXSYMBOLS * 2)];
    private readonly ushort[] _alignedTable = new ushort[(1 << LZX_ALIGNED_TABLEBITS) + (LZX_ALIGNED_MAXSYMBOLS * 2)];

    // E8 buffer for Intel transform
    private readonly byte[] _e8Buf = new byte[LZX_FRAME_SIZE];

    // I/O
    private Stream? _input;
    private Stream? _output;
    private long _offset;
    private long _length;

    public LzxDecompressor(int windowBits)
    {
        if (windowBits < 15 || windowBits > 21)
            throw new ArgumentException("Window bits must be between 15 and 21", nameof(windowBits));

        StaticInit();

        _windowBits = windowBits;
        _windowSize = 1u << windowBits;

        // Position slots calculation
        _posnSlots = windowBits switch
        {
            21 => 50,
            20 => 42,
            _ => windowBits << 1
        };
    }

    private static void StaticInit()
    {
        if (_staticInitialized) return;

        int j = 0;
        for (int i = 0; i < 51; i += 2)
        {
            ExtraBits[i] = (byte)j;
            ExtraBits[i + 1] = (byte)j;
            if (i != 0 && j < 17) j++;
        }

        j = 0;
        for (int i = 0; i < 51; i++)
        {
            PositionBase[i] = (uint)j;
            j += 1 << ExtraBits[i];
        }

        _staticInitialized = true;
    }

    private void ResetState()
    {
        _R0 = 1;
        _R1 = 1;
        _R2 = 1;
        _headerRead = false;
        _blockRemaining = 0;
        _blockType = LZX_BLOCKTYPE_INVALID;

        Array.Clear(_maintreeLen, 0, LZX_MAINTREE_MAXSYMBOLS);
        Array.Clear(_lengthLen, 0, LZX_LENGTH_MAXSYMBOLS);
    }

    public void Decompress(Stream input, Stream output, Action<string>? logCallback = null)
    {
        _input = input;
        _output = output;
        _offset = 0;
        _length = 100 * 1024 * 1024; // Max 100MB

        _window = new byte[_windowSize];
        _inbuf = new byte[4096];

        _windowPosn = 0;
        _framePosn = 0;
        _frame = 0;
        _resetInterval = 0;
        _intelFilesize = 0;
        _intelCurpos = 0;
        _intelStarted = false;
        _inputEnd = false;

        _iPtr = 0;
        _iEnd = 0;
        _bitBuffer = 0;
        _bitsLeft = 0;

        ResetState();

        try
        {
            DecompressInternal(_length, logCallback);
        }
        catch (EndOfStreamException)
        {
            // Normal end of stream
            logCallback?.Invoke("End of compressed data reached");
        }
    }

    private void DecompressInternal(long outBytes, Action<string>? logCallback)
    {
        uint endFrame = (uint)((_offset + outBytes) / LZX_FRAME_SIZE) + 1;

        while (_frame < endFrame)
        {
            // Reset interval handling
            if (_resetInterval != 0 && (_frame % _resetInterval) == 0)
            {
                if (_blockRemaining != 0)
                    throw new InvalidDataException("Bytes remaining at reset interval");
                ResetState();
            }

            // Read header if necessary
            if (!_headerRead)
            {
                int i = ReadBits(1);
                int j = 0;
                if (i != 0)
                {
                    i = ReadBits(16);
                    j = ReadBits(16);
                }
                _intelFilesize = (i << 16) | j;
                _headerRead = true;
            }

            // Calculate frame size
            uint frameSize = LZX_FRAME_SIZE;
            if (_length > 0 && (_length - _offset) < frameSize)
            {
                frameSize = (uint)(_length - _offset);
            }

            // Decode until one more frame is available
            int bytesTodo = (int)(_framePosn + frameSize - _windowPosn);

            while (bytesTodo > 0)
            {
                // Initialize new block if needed
                if (_blockRemaining == 0)
                {
                    // Realign if previous block was odd-sized uncompressed
                    if (_blockType == LZX_BLOCKTYPE_UNCOMPRESSED && (_blockLength & 1) != 0)
                    {
                        if (_iPtr >= _iEnd)
                            ReadInput();
                        _iPtr++;
                    }

                    // Read block type and length
                    _blockType = ReadBits(3);
                    int len1 = ReadBits(16);
                    int len2 = ReadBits(8);
                    _blockRemaining = _blockLength = (uint)((len1 << 8) | len2);

                    // Read individual block headers
                    switch (_blockType)
                    {
                        case LZX_BLOCKTYPE_ALIGNED:
                            for (int i = 0; i < 8; i++)
                            {
                                _alignedLen[i] = (byte)ReadBits(3);
                            }
                            BuildTable(LZX_ALIGNED_MAXSYMBOLS, LZX_ALIGNED_TABLEBITS, _alignedLen, _alignedTable);
                            goto case LZX_BLOCKTYPE_VERBATIM;

                        case LZX_BLOCKTYPE_VERBATIM:
                            ReadLengths(_maintreeLen, 0, 256);
                            ReadLengths(_maintreeLen, 256, LZX_NUM_CHARS + (_posnSlots << 3));
                            BuildTable(LZX_MAINTREE_MAXSYMBOLS, LZX_MAINTREE_TABLEBITS, _maintreeLen, _maintreeTable);

                            if (_maintreeLen[0xE8] != 0)
                                _intelStarted = true;

                            ReadLengths(_lengthLen, 0, LZX_NUM_SECONDARY_LENGTHS);
                            BuildTable(LZX_LENGTH_MAXSYMBOLS, LZX_LENGTH_TABLEBITS, _lengthLen, _lengthTable);
                            break;

                        case LZX_BLOCKTYPE_UNCOMPRESSED:
                            _intelStarted = true;

                            EnsureBits(16);
                            if (_bitsLeft > 16) _iPtr -= 2;
                            _bitsLeft = 0;
                            _bitBuffer = 0;

                            // Read R0, R1, R2
                            byte[] buf = new byte[12];
                            for (int i = 0; i < 12; i++)
                            {
                                if (_iPtr >= _iEnd)
                                    ReadInput();
                                buf[i] = _inbuf[_iPtr++];
                            }
                            _R0 = (uint)(buf[0] | (buf[1] << 8) | (buf[2] << 16) | (buf[3] << 24));
                            _R1 = (uint)(buf[4] | (buf[5] << 8) | (buf[6] << 16) | (buf[7] << 24));
                            _R2 = (uint)(buf[8] | (buf[9] << 8) | (buf[10] << 16) | (buf[11] << 24));
                            break;

                        default:
                            throw new InvalidDataException($"Bad block type: {_blockType}");
                    }
                }

                // Decode more of the block
                int thisRun = (int)Math.Min(_blockRemaining, (uint)bytesTodo);
                bytesTodo -= thisRun;
                _blockRemaining -= (uint)thisRun;

                switch (_blockType)
                {
                    case LZX_BLOCKTYPE_VERBATIM:
                        DecodeVerbatim(ref thisRun);
                        break;

                    case LZX_BLOCKTYPE_ALIGNED:
                        DecodeAligned(ref thisRun);
                        break;

                    case LZX_BLOCKTYPE_UNCOMPRESSED:
                        DecodeUncompressed(thisRun);
                        break;

                    default:
                        throw new InvalidDataException($"Bad block type: {_blockType}");
                }

                // Handle overrun
                if (thisRun < 0)
                {
                    if ((uint)(-thisRun) > _blockRemaining)
                        throw new InvalidDataException("Overrun went past end of block");
                    _blockRemaining -= (uint)(-thisRun);
                }
            }

            // Streams don't extend over frame boundaries
            if ((_windowPosn - _framePosn) != frameSize)
            {
                throw new InvalidDataException($"Decode beyond output frame limits: {_windowPosn - _framePosn} != {frameSize}");
            }

            // Re-align input bitstream
            if (_bitsLeft > 0) EnsureBits(16);
            if ((_bitsLeft & 15) != 0) RemoveBits(_bitsLeft & 15);

            // Intel E8 decoding
            byte[] outputData;
            if (_intelStarted && _intelFilesize != 0 && _frame <= 32768 && frameSize > 10)
            {
                Array.Copy(_window, _framePosn, _e8Buf, 0, (int)frameSize);
                DecodeE8(_e8Buf, (int)frameSize);
                outputData = _e8Buf;
            }
            else
            {
                outputData = _window;
                if (_intelFilesize != 0) _intelCurpos += (int)frameSize;
            }

            // Write frame
            int toWrite = (int)Math.Min(outBytes, frameSize);
            if (_intelStarted && _intelFilesize != 0 && _frame <= 32768 && frameSize > 10)
            {
                _output!.Write(outputData, 0, toWrite);
            }
            else
            {
                _output!.Write(outputData, (int)_framePosn, toWrite);
            }

            _offset += toWrite;
            outBytes -= toWrite;

            // Advance frame position
            _framePosn += frameSize;
            _frame++;

            // Wrap window/frame position
            if (_windowPosn == _windowSize) _windowPosn = 0;
            if (_framePosn == _windowSize) _framePosn = 0;

            // Check if we've written enough
            if (outBytes <= 0) break;
        }
    }

    private void DecodeVerbatim(ref int thisRun)
    {
        while (thisRun > 0)
        {
            int mainElement = ReadHuffSym(_maintreeTable, _maintreeLen, LZX_MAINTREE_TABLEBITS, LZX_MAINTREE_MAXSYMBOLS);

            if (mainElement < LZX_NUM_CHARS)
            {
                _window[_windowPosn++] = (byte)mainElement;
                thisRun--;
            }
            else
            {
                mainElement -= LZX_NUM_CHARS;

                int matchLength = mainElement & LZX_NUM_PRIMARY_LENGTHS;
                if (matchLength == LZX_NUM_PRIMARY_LENGTHS)
                {
                    int lengthFooter = ReadHuffSym(_lengthTable, _lengthLen, LZX_LENGTH_TABLEBITS, LZX_LENGTH_MAXSYMBOLS);
                    matchLength += lengthFooter;
                }
                matchLength += LZX_MIN_MATCH;

                uint matchOffset = (uint)(mainElement >> 3);
                switch (matchOffset)
                {
                    case 0: matchOffset = _R0; break;
                    case 1: matchOffset = _R1; _R1 = _R0; _R0 = matchOffset; break;
                    case 2: matchOffset = _R2; _R2 = _R0; _R0 = matchOffset; break;
                    case 3: matchOffset = 1; _R2 = _R1; _R1 = _R0; _R0 = matchOffset; break;
                    default:
                        int extra = ExtraBits[matchOffset];
                        int verbatimBits = ReadBits(extra);
                        matchOffset = PositionBase[matchOffset] - 2 + (uint)verbatimBits;
                        _R2 = _R1; _R1 = _R0; _R0 = matchOffset;
                        break;
                }

                if (_windowPosn + matchLength > _windowSize)
                    throw new InvalidDataException("Match ran over window wrap");

                CopyMatch((int)matchOffset, matchLength);
                thisRun -= matchLength;
            }
        }
    }

    private void DecodeAligned(ref int thisRun)
    {
        while (thisRun > 0)
        {
            int mainElement = ReadHuffSym(_maintreeTable, _maintreeLen, LZX_MAINTREE_TABLEBITS, LZX_MAINTREE_MAXSYMBOLS);

            if (mainElement < LZX_NUM_CHARS)
            {
                _window[_windowPosn++] = (byte)mainElement;
                thisRun--;
            }
            else
            {
                mainElement -= LZX_NUM_CHARS;

                int matchLength = mainElement & LZX_NUM_PRIMARY_LENGTHS;
                if (matchLength == LZX_NUM_PRIMARY_LENGTHS)
                {
                    int lengthFooter = ReadHuffSym(_lengthTable, _lengthLen, LZX_LENGTH_TABLEBITS, LZX_LENGTH_MAXSYMBOLS);
                    matchLength += lengthFooter;
                }
                matchLength += LZX_MIN_MATCH;

                uint matchOffset = (uint)(mainElement >> 3);
                switch (matchOffset)
                {
                    case 0: matchOffset = _R0; break;
                    case 1: matchOffset = _R1; _R1 = _R0; _R0 = matchOffset; break;
                    case 2: matchOffset = _R2; _R2 = _R0; _R0 = matchOffset; break;
                    default:
                        int extra = ExtraBits[matchOffset];
                        matchOffset = PositionBase[matchOffset] - 2;

                        if (extra > 3)
                        {
                            extra -= 3;
                            int verbatimBits = ReadBits(extra);
                            matchOffset += (uint)(verbatimBits << 3);
                            int alignedBits = ReadHuffSym(_alignedTable, _alignedLen, LZX_ALIGNED_TABLEBITS, LZX_ALIGNED_MAXSYMBOLS);
                            matchOffset += (uint)alignedBits;
                        }
                        else if (extra == 3)
                        {
                            int alignedBits = ReadHuffSym(_alignedTable, _alignedLen, LZX_ALIGNED_TABLEBITS, LZX_ALIGNED_MAXSYMBOLS);
                            matchOffset += (uint)alignedBits;
                        }
                        else if (extra > 0)
                        {
                            int verbatimBits = ReadBits(extra);
                            matchOffset += (uint)verbatimBits;
                        }
                        else
                        {
                            matchOffset = 1;
                        }
                        _R2 = _R1; _R1 = _R0; _R0 = matchOffset;
                        break;
                }

                if (_windowPosn + matchLength > _windowSize)
                    throw new InvalidDataException("Match ran over window wrap");

                CopyMatch((int)matchOffset, matchLength);
                thisRun -= matchLength;
            }
        }
    }

    private void DecodeUncompressed(int thisRun)
    {
        uint destPos = _windowPosn;
        _windowPosn += (uint)thisRun;

        while (thisRun > 0)
        {
            int available = _iEnd - _iPtr;
            if (available > 0)
            {
                int toCopy = Math.Min(available, thisRun);
                Array.Copy(_inbuf, _iPtr, _window, destPos, toCopy);
                destPos += (uint)toCopy;
                _iPtr += toCopy;
                thisRun -= toCopy;
            }
            else
            {
                ReadInput();
            }
        }
    }

    private void CopyMatch(int matchOffset, int matchLength)
    {
        uint destPos = _windowPosn;
        _windowPosn += (uint)matchLength;

        if (matchOffset > destPos)
        {
            int j = matchOffset - (int)destPos;
            if (j > _windowSize)
                throw new InvalidDataException("Match offset beyond window boundaries");

            uint srcPos = _windowSize - (uint)j;
            if (j < matchLength)
            {
                matchLength -= j;
                while (j-- > 0)
                    _window[destPos++] = _window[srcPos++];
                srcPos = 0;
            }
            while (matchLength-- > 0)
                _window[destPos++] = _window[srcPos++];
        }
        else
        {
            uint srcPos = destPos - (uint)matchOffset;
            while (matchLength-- > 0)
                _window[destPos++] = _window[srcPos++];
        }
    }

    private void DecodeE8(byte[] data, int size)
    {
        int curpos = _intelCurpos;
        int filesize = _intelFilesize;
        int dataEnd = size - 10;

        for (int i = 0; i < dataEnd;)
        {
            if (data[i++] != 0xE8)
            {
                curpos++;
                continue;
            }

            int absOff = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24);
            if (absOff >= -curpos && absOff < filesize)
            {
                int relOff = absOff >= 0 ? absOff - curpos : absOff + filesize;
                data[i] = (byte)relOff;
                data[i + 1] = (byte)(relOff >> 8);
                data[i + 2] = (byte)(relOff >> 16);
                data[i + 3] = (byte)(relOff >> 24);
            }
            i += 4;
            curpos += 5;
        }
        _intelCurpos += size;
    }

    private void ReadInput()
    {
        int read = _input!.Read(_inbuf, 0, _inbuf.Length);
        if (read <= 0)
        {
            if (_inputEnd)
                throw new EndOfStreamException("Out of input bytes");

            read = 2;
            _inbuf[0] = _inbuf[1] = 0;
            _inputEnd = true;
        }

        _iPtr = 0;
        _iEnd = read;
    }

    private void EnsureBits(int nbits)
    {
        while (_bitsLeft < nbits)
        {
            if (_iPtr >= _iEnd)
                ReadInput();

            // Read 16 bits in little-endian, but store big-endian in buffer
            int word = (_inbuf[_iPtr + 1] << 8) | _inbuf[_iPtr];
            _bitBuffer |= (uint)word << (32 - 16 - _bitsLeft);
            _bitsLeft += 16;
            _iPtr += 2;
        }
    }

    private int PeekBits(int nbits)
    {
        return (int)(_bitBuffer >> (32 - nbits));
    }

    private void RemoveBits(int nbits)
    {
        _bitBuffer <<= nbits;
        _bitsLeft -= nbits;
    }

    private int ReadBits(int nbits)
    {
        EnsureBits(nbits);
        int val = PeekBits(nbits);
        RemoveBits(nbits);
        return val;
    }

    private int ReadHuffSym(ushort[] table, byte[] lengths, int tablebits, int maxSymbols)
    {
        EnsureBits(16);
        int sym = table[PeekBits(tablebits)];

        if (sym >= maxSymbols)
        {
            uint mask = 1u << (32 - tablebits);
            do
            {
                mask >>= 1;
                if (mask == 0)
                    throw new InvalidDataException("Out of bits in huffman decode");
                sym <<= 1;
                sym |= (_bitBuffer & mask) != 0 ? 1 : 0;
                sym = table[sym];
            } while (sym >= maxSymbols);
        }

        int len = lengths[sym];
        RemoveBits(len);
        return sym;
    }

    private void ReadLengths(byte[] lens, int first, int last)
    {
        // Read pretree
        for (int x = 0; x < 20; x++)
        {
            _pretreeLen[x] = (byte)ReadBits(4);
        }
        BuildTable(LZX_PRETREE_MAXSYMBOLS, LZX_PRETREE_TABLEBITS, _pretreeLen, _pretreeTable);

        for (int x = first; x < last;)
        {
            int z = ReadHuffSym(_pretreeTable, _pretreeLen, LZX_PRETREE_TABLEBITS, LZX_PRETREE_MAXSYMBOLS);

            if (z == 17)
            {
                int y = ReadBits(4) + 4;
                while (y-- > 0) lens[x++] = 0;
            }
            else if (z == 18)
            {
                int y = ReadBits(5) + 20;
                while (y-- > 0) lens[x++] = 0;
            }
            else if (z == 19)
            {
                int y = ReadBits(1) + 4;
                z = ReadHuffSym(_pretreeTable, _pretreeLen, LZX_PRETREE_TABLEBITS, LZX_PRETREE_MAXSYMBOLS);
                z = lens[x] - z;
                if (z < 0) z += 17;
                while (y-- > 0) lens[x++] = (byte)z;
            }
            else
            {
                z = lens[x] - z;
                if (z < 0) z += 17;
                lens[x++] = (byte)z;
            }
        }
    }

    private static void BuildTable(int nsyms, int nbits, byte[] lengths, ushort[] table)
    {
        uint pos = 0;
        uint tableMask = 1u << nbits;
        uint bitMask = tableMask >> 1;
        uint nextSymbol = bitMask;

        // Fill entries for codes short enough for direct mapping
        for (int bitNum = 1; bitNum <= nbits; bitNum++)
        {
            for (ushort sym = 0; sym < nsyms; sym++)
            {
                if (lengths[sym] != bitNum) continue;
                uint leaf = pos;
                pos += bitMask;
                if (pos > tableMask)
                    throw new InvalidDataException("Table overrun");
                for (uint fill = bitMask; fill-- > 0;)
                    table[leaf++] = sym;
            }
            bitMask >>= 1;
        }

        if (pos == tableMask) return;

        // Clear remainder
        for (uint sym = pos; sym < tableMask; sym++)
            table[sym] = 0xFFFF;

        // Allow codes to be up to nbits+16 long
        pos <<= 16;
        tableMask <<= 16;
        bitMask = 1 << 15;

        for (int bitNum = nbits + 1; bitNum <= 16; bitNum++)
        {
            for (ushort sym = 0; sym < nsyms; sym++)
            {
                if (lengths[sym] != bitNum) continue;

                uint leaf = pos >> 16;
                for (int fill = 0; fill < bitNum - nbits; fill++)
                {
                    if (table[leaf] == 0xFFFF)
                    {
                        table[nextSymbol << 1] = 0xFFFF;
                        table[(nextSymbol << 1) + 1] = 0xFFFF;
                        table[leaf] = (ushort)nextSymbol++;
                    }
                    leaf = (uint)(table[leaf] << 1);
                    if (((pos >> (15 - fill)) & 1) != 0) leaf++;
                }
                table[leaf] = sym;

                pos += bitMask;
                if (pos > tableMask)
                    throw new InvalidDataException("Table overflow");
            }
            bitMask >>= 1;
        }

        if (pos == tableMask) return;

        // Check for erroneous table or all-zero elements
        for (int sym = 0; sym < nsyms; sym++)
        {
            if (lengths[sym] != 0)
                throw new InvalidDataException("Invalid huffman table");
        }
    }
}
