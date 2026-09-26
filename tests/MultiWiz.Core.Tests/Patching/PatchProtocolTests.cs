using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MultiWiz.Core.Patching;

namespace MultiWiz.Core.Tests.Patching;

public sealed class PatchProtocolTests
{
    [Fact]
    public async Task Frames_use_magic_length_and_a_trailing_zero()
    {
        using var stream = new MemoryStream();
        await PatchProtocol.WriteFrameAsync(stream, isControl: true, 5, new byte[] { 0xAA, 0xBB }, CancellationToken.None);

        Assert.Equal(new byte[] { 0x0D, 0xF0, 0x07, 0x00, 0x01, 0x05, 0x00, 0x00, 0xAA, 0xBB, 0x00 }, stream.ToArray());

        stream.Position = 0;
        var frame = await PatchProtocol.ReadFrameAsync(stream, CancellationToken.None);
        Assert.True(frame.IsControl);
        Assert.Equal((byte)5, frame.Opcode);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, frame.Payload);
    }

    [Fact]
    public async Task Large_frames_use_the_extended_length()
    {
        var payload = new byte[0x9000];
        new Random(1).NextBytes(payload);
        using var stream = new MemoryStream();
        await PatchProtocol.WriteFrameAsync(stream, isControl: false, 0, payload, CancellationToken.None);

        var bytes = stream.ToArray();
        Assert.Equal((ushort)0x8000, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        Assert.Equal((uint)(4 + payload.Length + 1), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));

        stream.Position = 0;
        var frame = await PatchProtocol.ReadFrameAsync(stream, CancellationToken.None);
        Assert.False(frame.IsControl);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task Rejects_bad_magic()
    {
        using var stream = new MemoryStream([0x0D, 0xF1, 0x05, 0x00, 1, 0, 0, 0, 0]);
        await Assert.ThrowsAnyAsync<PatchServerException>(() => PatchProtocol.ReadFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Handshake_accepts_the_session_answers_keepalives_and_reads_the_file_list()
    {
        var server = new MemoryStream();
        await PatchProtocol.WriteFrameAsync(server, true, PatchProtocol.OpcodeSessionOffer, SessionOffer(sessionId: 0x1234, seconds: 1_700_000_000, millis: 321), CancellationToken.None);
        await PatchProtocol.WriteFrameAsync(server, true, PatchProtocol.OpcodeKeepAlive, new byte[6], CancellationToken.None);
        await PatchProtocol.WriteFrameAsync(server, false, 0, PatchProtocol.BuildDmlMessage(7, 1, new byte[] { 1, 2, 3 }), CancellationToken.None);
        await PatchProtocol.WriteFrameAsync(server, false, 0, PatchProtocol.BuildDmlMessage(8, 2, FileListAnswer()), CancellationToken.None);
        var duplex = new ScriptedDuplexStream(server.ToArray());

        var info = await PatchProtocol.RequestLatestFileListAsync(duplex, CancellationToken.None);

        Assert.Equal(812u, info.LatestVersion);
        Assert.Equal("LatestFileList.bin", info.ListFileName);
        Assert.Equal(4u, info.ListFileType);
        Assert.Equal(1111u, info.ListFileTime);
        Assert.Equal(2222u, info.ListFileSize);
        Assert.Equal(0xDEADBEEFu, info.ListFileCrc);
        Assert.Equal("http://cdn.example.test/patch/r812/Windows/LatestFileList.bin", info.ListFileUrl);
        Assert.Equal("http://cdn.example.test/patch/r812/LatestBuild", info.UrlPrefix);
        Assert.Equal("", info.UrlSuffix);
        Assert.Equal("English", info.Locale);

        // What the client sent: SessionAccept echoing the offer, the file list request, then a keep-alive reply.
        var sent = new MemoryStream(duplex.Written.ToArray());
        var accept = await PatchProtocol.ReadFrameAsync(sent, CancellationToken.None);
        Assert.True(accept.IsControl);
        Assert.Equal(PatchProtocol.OpcodeSessionAccept, accept.Opcode);
        Assert.Equal(1_700_000_000u, BinaryPrimitives.ReadUInt32LittleEndian(accept.Payload.AsSpan(6)));
        Assert.Equal(321u, BinaryPrimitives.ReadUInt32LittleEndian(accept.Payload.AsSpan(10)));
        Assert.Equal((ushort)0x1234, BinaryPrimitives.ReadUInt16LittleEndian(accept.Payload.AsSpan(14)));

        var request = await PatchProtocol.ReadFrameAsync(sent, CancellationToken.None);
        Assert.False(request.IsControl);
        Assert.Equal((byte)8, request.Payload[0]);
        Assert.Equal((byte)2, request.Payload[1]);
        Assert.Equal((ushort)(4 + 30), BinaryPrimitives.ReadUInt16LittleEndian(request.Payload.AsSpan(2)));
        Assert.True(request.Payload.AsSpan(4).IndexOfAnyExcept((byte)0) < 0);

        var keepAlive = await PatchProtocol.ReadFrameAsync(sent, CancellationToken.None);
        Assert.True(keepAlive.IsControl);
        Assert.Equal(PatchProtocol.OpcodeKeepAliveResponse, keepAlive.Opcode);
        Assert.Empty(keepAlive.Payload);
    }

    [Fact]
    public async Task Client_reports_a_closed_connection_as_a_patch_server_error()
    {
        var server = new MemoryStream();
        await PatchProtocol.WriteFrameAsync(server, true, PatchProtocol.OpcodeSessionOffer, SessionOffer(1, 2, 3), CancellationToken.None);
        var client = new PatchServerClient(new FakeConnections(() => new ScriptedDuplexStream(server.ToArray())), NullLogger<PatchServerClient>.Instance);

        await Assert.ThrowsAnyAsync<PatchServerException>(() => client.GetLatestFileListAsync(PatchServers.Wizard101));
    }

    [Fact]
    public async Task Client_times_out_when_the_server_is_silent()
    {
        var client = new PatchServerClient(new FakeConnections(() => new ScriptedDuplexStream([], blockAtEnd: true)), NullLogger<PatchServerClient>.Instance)
        {
            Timeout = TimeSpan.FromMilliseconds(200),
        };

        var error = await Assert.ThrowsAnyAsync<PatchServerException>(() => client.GetLatestFileListAsync(PatchServers.Wizard101));
        Assert.Contains("didn't answer in time", error.Message);
    }

    [Fact]
    public void Wizard101_uses_the_windows_patch_tree_and_pirate101_is_unknown()
    {
        Assert.Equal(new PatchServerEndpoint("patch.us.wizard101.com", 12500), PatchServers.For(Games.GameKind.Wizard101));
        Assert.Null(PatchServers.For(Games.GameKind.Pirate101));
    }

    internal static byte[] SessionOffer(ushort sessionId, uint seconds, uint millis)
    {
        var buffer = new byte[2 + 4 + 4 + 4 + 4 + 3 + 1];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(6), seconds);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(10), millis);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(14), 3);
        return buffer;
    }

    internal static byte[] FileListAnswer()
    {
        using var output = new MemoryStream();
        DmlBuilder.WriteU32(output, 812);
        DmlBuilder.WriteString(output, "LatestFileList.bin");
        DmlBuilder.WriteU32(output, 4);
        DmlBuilder.WriteU32(output, 1111);
        DmlBuilder.WriteU32(output, 2222);
        DmlBuilder.WriteU32(output, 0xDEADBEEF);
        DmlBuilder.WriteString(output, "http://cdn.example.test/patch/r812/Windows/LatestFileList.bin");
        DmlBuilder.WriteString(output, "http://cdn.example.test/patch/r812/LatestBuild");
        DmlBuilder.WriteString(output, "");
        DmlBuilder.WriteString(output, "English");
        return output.ToArray();
    }

    private sealed class FakeConnections(Func<Stream> create) : IPatchConnectionFactory
    {
        public Task<Stream> ConnectAsync(PatchServerEndpoint endpoint, CancellationToken cancellationToken) => Task.FromResult(create());
    }

    /// <summary>Reads come from a script of server bytes; writes are captured. Optionally blocks instead of ending.</summary>
    private sealed class ScriptedDuplexStream(byte[] serverBytes, bool blockAtEnd = false) : Stream
    {
        private readonly MemoryStream _read = new(serverBytes);

        public MemoryStream Written { get; } = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = _read.Read(buffer.Span);
            if (read == 0 && blockAtEnd)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => _read.Read(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
