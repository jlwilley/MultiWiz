using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using MultiWiz.Core.Patching;

namespace MultiWiz.Core.Tests.Patching;

/// <summary>Builds synthetic DML binary tables in the LatestFileList.bin format.</summary>
internal static class DmlBuilder
{
    public const byte U32 = 3;
    public const byte Str = 9;
    public const byte U8 = 5;
    public const byte U16 = 6;
    public const byte U64 = 0;

    public static void WriteTable(MemoryStream output, string tableName, IReadOnlyList<(string Name, byte Type)> fields, IEnumerable<object[]> rows)
    {
        var rowList = rows.ToList();
        WriteU32(output, (uint)rowList.Count);
        output.Write([0x02, 0x01]);
        WriteU16(output, 0);
        foreach (var (name, type) in fields)
        {
            WriteString(output, name);
            output.Write([type, 0x28]);
        }

        WriteString(output, "_TargetTable");
        output.Write([Str, 0x28]);
        WriteString(output, tableName);

        foreach (var row in rowList)
        {
            output.Write([0x02, 0x02]);
            WriteU16(output, 0);
            for (var i = 0; i < fields.Count; i++)
            {
                switch (fields[i].Type)
                {
                    case U32: WriteU32(output, Convert.ToUInt32(row[i])); break;
                    case U8: output.WriteByte(Convert.ToByte(row[i])); break;
                    case U16: WriteU16(output, Convert.ToUInt16(row[i])); break;
                    case U64:
                        var buffer = new byte[8];
                        BinaryPrimitives.WriteUInt64LittleEndian(buffer, Convert.ToUInt64(row[i]));
                        output.Write(buffer);
                        break;
                    default: WriteString(output, (string)row[i]); break;
                }
            }
        }
    }

    public static void WriteString(Stream output, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteU16(output, (ushort)bytes.Length);
        output.Write(bytes);
    }

    public static void WriteU16(Stream output, ushort value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        output.Write(buffer);
    }

    public static void WriteU32(Stream output, uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        output.Write(buffer);
    }
}

/// <summary>A file served by the fake CDN and listed in the fake file list.</summary>
internal sealed record FakeFile(string Package, string Source, byte[] Content, string Target = "", uint FileType = 1)
{
    public uint Crc => KiCrc32.Compute(Content);
}

/// <summary>Builds a LatestFileList.bin with a _TableList meta table and one table per package.</summary>
internal static class FileListBuilder
{
    public static readonly (string, byte)[] FileFields =
    [
        ("SrcFileName", DmlBuilder.Str), ("TarFileName", DmlBuilder.Str), ("FileType", DmlBuilder.U32), ("Size", DmlBuilder.U32),
        ("HeaderSize", DmlBuilder.U32), ("CompressedHeaderSize", DmlBuilder.U32), ("CRC", DmlBuilder.U32), ("HeaderCRC", DmlBuilder.U32),
    ];

    public static byte[] Build(IEnumerable<FakeFile> files)
    {
        using var output = new MemoryStream();
        var byPackage = files.GroupBy(file => file.Package).ToList();
        DmlBuilder.WriteTable(output, "_TableList", [("Name", DmlBuilder.Str)], byPackage.Select(group => new object[] { group.Key }));
        foreach (var group in byPackage)
        {
            DmlBuilder.WriteTable(output, group.Key, FileFields, group.Select(file => new object[]
            {
                file.Source, file.Target, file.FileType, (uint)file.Content.Length, 0u, 0u, file.Crc, 0u,
            }));
        }

        return output.ToArray();
    }
}

internal sealed class FakePatchServerClient(Func<LatestFileListInfo> answer) : IPatchServerClient
{
    public int Calls;

    public Task<LatestFileListInfo> GetLatestFileListAsync(PatchServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(answer());
    }
}

/// <summary>An HTTP handler serving canned responses per URL, optionally a scripted sequence per URL.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes = new();
    private readonly ConcurrentDictionary<string, int> _hits = new();

    public ConcurrentQueue<(string Url, string? UserAgent)> Requests { get; } = new();

    public void Serve(string url, byte[] content) => _routes[url] = (_, _) => Task.FromResult(Ok(content));

    public void Serve(string url, Func<int, HttpResponseMessage> byAttempt) =>
        _routes[url] = (request, _) => Task.FromResult(byAttempt(_hits.GetValueOrDefault(request.RequestUri!.ToString())));

    public void Serve(string url, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _routes[url] = handler;

    public int Hits(string url) => _hits.GetValueOrDefault(url);

    public static HttpResponseMessage Ok(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Enqueue((url, request.Headers.TryGetValues("User-Agent", out var values) ? string.Join(" ", values) : null));
        if (!_routes.TryGetValue(url, out var route))
        {
            _hits.AddOrUpdate(url, 1, (_, n) => n + 1);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var response = await route(request, cancellationToken);
        _hits.AddOrUpdate(url, 1, (_, n) => n + 1);
        return response;
    }
}

/// <summary>Returns some bytes, then blocks until cancelled.</summary>
internal sealed class StallingStream(byte[] prefix) : Stream
{
    private int _position;

    public TaskCompletionSource Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < prefix.Length)
        {
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        Stalled.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
