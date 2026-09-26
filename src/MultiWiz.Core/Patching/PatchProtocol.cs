using System.Buffers.Binary;
using System.Text;

namespace MultiWiz.Core.Patching;

/// <summary>
/// The small slice of KingsIsle's network protocol needed to ask a patch server for its latest file list.
/// </summary>
/// <remarks>
/// Every frame is <c>F0 0D</c> (0xF00D, little-endian u16), a u16 length (0x8000 or more means a u32 length follows)
/// counting the body plus one trailing zero byte, then the body and that zero byte. A body is
/// <c>[isControl u8][opcode u8][0 u16][payload]</c>. The server opens with a SessionOffer control message (opcode 0:
/// session id u16, 4 unknown bytes, time secs u32, time millis u32, signed blob); the client answers with a
/// SessionAccept (opcode 5) echoing the session id and times. Application ("DML") messages are non-control frames whose
/// payload is <c>[service u8][order u8][length u16 = 4 + data length][data]</c>. The patch service is 8; message 2 is
/// LatestFileListV2, whose fields are, in order: LatestVersion u32, ListFileName str, ListFileType u32,
/// ListFileTime u32, ListFileSize u32, ListFileCRC u32, ListFileURL str, URLPrefix str, URLSuffix str, Locale str
/// (strings are a u16 byte length followed by the bytes). The request is the same message with every field empty.
/// Keep-alives (control opcode 3) are answered with opcode 4 and an empty body.
/// Behaviour matches the Go clients used by sigil-w101launcher (MIT) and umbra-launcher; this is an independent
/// implementation.
/// </remarks>
internal static class PatchProtocol
{
    public const ushort Magic = 0xF00D;
    public const byte OpcodeSessionOffer = 0;
    public const byte OpcodeKeepAlive = 3;
    public const byte OpcodeKeepAliveResponse = 4;
    public const byte OpcodeSessionAccept = 5;
    public const byte PatchService = 8;
    public const byte LatestFileListV2Order = 2;

    /// <summary>Largest frame accepted; the file list answer is a few hundred bytes.</summary>
    public const int MaxFrameLength = 1 << 20;

    /// <summary>Messages read before giving up on the file list answer.</summary>
    private const int MaxFramesBeforeAnswer = 64;

    public readonly record struct Frame(bool IsControl, byte Opcode, byte[] Payload);

    /// <summary>Performs the session handshake on <paramref name="stream"/> and returns the file list answer.</summary>
    public static async Task<LatestFileListInfo> RequestLatestFileListAsync(Stream stream, CancellationToken cancellationToken)
    {
        var requested = false;
        for (var i = 0; i < MaxFramesBeforeAnswer; i++)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame.IsControl)
            {
                switch (frame.Opcode)
                {
                    case OpcodeSessionOffer when !requested:
                        var offer = ParseSessionOffer(frame.Payload);
                        await WriteFrameAsync(stream, isControl: true, OpcodeSessionAccept, BuildSessionAccept(offer), cancellationToken).ConfigureAwait(false);
                        await WriteFrameAsync(stream, isControl: false, 0, BuildDmlMessage(PatchService, LatestFileListV2Order, new byte[EmptyLatestFileListV2Length]), cancellationToken).ConfigureAwait(false);
                        requested = true;
                        break;
                    case OpcodeKeepAlive:
                        await WriteFrameAsync(stream, isControl: true, OpcodeKeepAliveResponse, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                        break;
                }

                continue;
            }

            if (TryParseDml(frame.Payload, out var service, out var order, out var data)
                && service == PatchService && order == LatestFileListV2Order)
            {
                return ParseLatestFileListV2(data.Span);
            }
        }

        throw new PatchServerException("The patch server never sent its file list.");
    }

    public static async Task WriteFrameAsync(Stream stream, bool isControl, byte opcode, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var bodyLength = 4 + payload.Length;
        var lengthWithTerminator = bodyLength + 1;
        var large = lengthWithTerminator > 0x7FFF;
        var headerLength = large ? 8 : 4;
        var buffer = new byte[headerLength + bodyLength + 1];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, Magic);
        if (large)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 0x8000);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)lengthWithTerminator);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)lengthWithTerminator);
        }

        buffer[headerLength] = isControl ? (byte)1 : (byte)0;
        buffer[headerLength + 1] = opcode;
        payload.Span.CopyTo(buffer.AsSpan(headerLength + 4));
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Frame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt16LittleEndian(header) != Magic)
        {
            throw new PatchServerException("The patch server sent data MultiWiz doesn't understand.");
        }

        long length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
        if (length >= 0x8000)
        {
            var extended = new byte[4];
            await stream.ReadExactlyAsync(extended, cancellationToken).ConfigureAwait(false);
            length = BinaryPrimitives.ReadUInt32LittleEndian(extended);
        }

        if (length < 5 || length > MaxFrameLength)
        {
            throw new PatchServerException("The patch server sent a malformed message.");
        }

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);

        // body = [isControl][opcode][2 reserved bytes][payload][terminating zero]
        return new Frame(body[0] == 1, body[1], body[4..^1]);
    }

    public static (ushort SessionId, uint TimeSeconds, uint TimeMilliseconds) ParseSessionOffer(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 14)
        {
            throw new PatchServerException("The patch server sent a malformed session offer.");
        }

        return (
            BinaryPrimitives.ReadUInt16LittleEndian(payload),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[6..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[10..]));
    }

    /// <summary>6 reserved bytes, time secs, time millis, session id, then a one-byte (zero) blob and a zero terminator.</summary>
    public static byte[] BuildSessionAccept((ushort SessionId, uint TimeSeconds, uint TimeMilliseconds) offer)
    {
        var buffer = new byte[6 + 4 + 4 + 2 + 4 + 1 + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(6), offer.TimeSeconds);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(10), offer.TimeMilliseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), offer.SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), 1);
        return buffer;
    }

    public static byte[] BuildDmlMessage(byte service, byte order, ReadOnlySpan<byte> data)
    {
        var buffer = new byte[4 + data.Length];
        buffer[0] = service;
        buffer[1] = order;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)(4 + data.Length));
        data.CopyTo(buffer.AsSpan(4));
        return buffer;
    }

    /// <summary>
    /// Splits a DML payload into service, order and data. The data runs to the end of the frame: the declared length is
    /// not trusted (the Go clients read one byte past it), and the frame length already bounds the message.
    /// </summary>
    public static bool TryParseDml(byte[] payload, out byte service, out byte order, out ReadOnlyMemory<byte> data)
    {
        service = 0;
        order = 0;
        data = default;
        if (payload.Length < 4)
        {
            return false;
        }

        service = payload[0];
        order = payload[1];
        data = payload.AsMemory(4);
        return true;
    }

    /// <summary>A LatestFileListV2 with every number zero and every string empty (5 u32 + 5 empty strings).</summary>
    public const int EmptyLatestFileListV2Length = 30;

    public static LatestFileListInfo ParseLatestFileListV2(ReadOnlySpan<byte> data)
    {
        var reader = new SpanReader(data);
        try
        {
            var version = reader.ReadUInt32();
            var listFileName = reader.ReadString();
            var listFileType = reader.ReadUInt32();
            var listFileTime = reader.ReadUInt32();
            var listFileSize = reader.ReadUInt32();
            var listFileCrc = reader.ReadUInt32();
            var listFileUrl = reader.ReadString();
            var urlPrefix = reader.ReadString();
            var urlSuffix = reader.ReadString();
            var locale = reader.Remaining >= 2 ? reader.ReadString() : "";
            return new LatestFileListInfo
            {
                LatestVersion = version,
                ListFileName = listFileName,
                ListFileType = listFileType,
                ListFileTime = listFileTime,
                ListFileSize = listFileSize,
                ListFileCrc = listFileCrc,
                ListFileUrl = listFileUrl,
                UrlPrefix = urlPrefix,
                UrlSuffix = urlSuffix,
                Locale = locale,
            };
        }
        catch (InvalidDataException ex)
        {
            throw new PatchServerException("The patch server's file list answer was malformed.", ex);
        }
    }

    /// <summary>Little-endian reader over a span that throws <see cref="InvalidDataException"/> past the end.</summary>
    internal ref struct SpanReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public readonly int Position => _position;

        public readonly int Remaining => _data.Length - _position;

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("Unexpected end of data.");
            }

            var slice = _data.Slice(_position, count);
            _position += count;
            return slice;
        }

        public void Skip(int count) => ReadBytes(count);

        public byte ReadByte() => ReadBytes(1)[0];

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));

        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(8));

        /// <summary>A u16 byte length followed by that many bytes (KingsIsle strings are ASCII in practice).</summary>
        public string ReadString() => Encoding.UTF8.GetString(ReadBytes(ReadUInt16()));
    }
}
