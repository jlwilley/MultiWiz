using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Games;

namespace MultiWiz.Core.Patching;

/// <summary>Opens a byte stream to a patch server. Replaced by an in-memory stream in tests.</summary>
public interface IPatchConnectionFactory
{
    Task<Stream> ConnectAsync(PatchServerEndpoint endpoint, CancellationToken cancellationToken);
}

/// <summary>Plain TCP connection (the patch protocol is not encrypted).</summary>
public sealed class TcpPatchConnectionFactory : IPatchConnectionFactory
{
    public async Task<Stream> ConnectAsync(PatchServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>Asks KingsIsle's patch server for the latest revision and file list location.</summary>
public interface IPatchServerClient
{
    /// <summary>Throws <see cref="PatchServerException"/> (user-readable message) when the server can't be reached or answers badly.</summary>
    Task<LatestFileListInfo> GetLatestFileListAsync(PatchServerEndpoint endpoint, CancellationToken cancellationToken = default);
}

public sealed class PatchServerClient : IPatchServerClient
{
    private readonly IPatchConnectionFactory _connections;
    private readonly ILogger<PatchServerClient> _logger;

    public PatchServerClient(IPatchConnectionFactory connections, ILogger<PatchServerClient> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    /// <summary>Limit for connecting, the handshake and the answer together.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public async Task<LatestFileListInfo> GetLatestFileListAsync(PatchServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var stream = await _connections.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var info = await PatchProtocol.RequestLatestFileListAsync(stream, timeout.Token).ConfigureAwait(false);
                _logger.LogInformation("Patch server {Endpoint}: revision {Revision}, file list {Url}", endpoint, info.LatestVersion, info.ListFileUrl);
                if (string.IsNullOrWhiteSpace(info.ListFileUrl) || string.IsNullOrWhiteSpace(info.UrlPrefix))
                {
                    throw new PatchServerException("The patch server's answer didn't say where to download the game files.");
                }

                return info;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PatchServerException($"KingsIsle's patch server ({endpoint}) didn't answer in time. Try again later.");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            _logger.LogWarning(ex, "Patch server {Endpoint} could not be reached", endpoint);
            throw new PatchServerException($"Couldn't reach KingsIsle's patch server ({endpoint}). Check your internet connection and try again.", ex);
        }
    }
}

/// <summary>Patch servers MultiWiz knows how to use.</summary>
public static class PatchServers
{
    /// <summary>The Windows file tree of the North American Wizard101 patch server (port 12600 serves the Mac tree).</summary>
    public static PatchServerEndpoint Wizard101 { get; } = new("patch.us.wizard101.com", 12500);

    /// <summary>Null when MultiWiz doesn't know a verified patch server for <paramref name="game"/>.</summary>
    public static PatchServerEndpoint? For(GameKind game) => game == GameKind.Wizard101 ? Wizard101 : null;
}
