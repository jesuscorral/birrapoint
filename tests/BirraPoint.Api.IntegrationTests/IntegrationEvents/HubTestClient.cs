using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.TestHost;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// Minimal SignalR JSON-protocol client over the in-process TestServer WebSocket, so the tests receive real
/// <c>CompetitionHub</c> pushes (contracts/signalr-hub.md) without a client package. The handshake skips
/// negotiation; the access token travels in <c>access_token</c> as the contract specifies.
/// </summary>
internal sealed class HubTestClient : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';

    private readonly WebSocket _socket;
    private readonly Channel<(string Target, JsonElement[] Arguments)> _messages = Channel.CreateUnbounded<(string, JsonElement[])>();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _completions = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private Task _receiveLoop = Task.CompletedTask;
    private int _invocationCounter;

    private HubTestClient(WebSocket socket) => _socket = socket;

    public static async Task<HubTestClient> ConnectAsync(TestServer server, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        var socket = await server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost/hubs/competition?access_token={Uri.EscapeDataString(accessToken)}"),
            TestContext.Current.CancellationToken);
        var client = new HubTestClient(socket);
        client._receiveLoop = client.ReceiveLoopAsync();
        await client.SendAsync("{\"protocol\":\"json\",\"version\":1}");
        await client._handshake.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        return client;
    }

    /// <summary>Invokes a hub method and waits for its completion; a hub exception fails the test.</summary>
    public async Task InvokeAsync(string method, params object[] arguments)
    {
        var id = Interlocked.Increment(ref _invocationCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _completions[id] = completion;
        await SendAsync(JsonSerializer.Serialize(new { type = 1, invocationId = id, target = method, arguments }));
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (result.TryGetProperty("error", out var error))
        {
            Assert.Fail($"Hub method {method} failed: {error.GetString()}");
        }
    }

    /// <summary>The first argument of the next <paramref name="target"/> push, or null when none arrives in time.</summary>
    public async Task<JsonElement?> NextAsync(string target, TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var (name, arguments) = await _messages.Reader.ReadAsync(cts.Token);
                if (string.Equals(name, target, StringComparison.Ordinal))
                {
                    return arguments.Length > 0 ? arguments[0] : default(JsonElement);
                }
            }
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The host may already be gone; the test is over either way.
            }
        }

        try
        {
            await _receiveLoop;
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop is cancelled with the client.
        }

        _socket.Dispose();
        _lifetime.Dispose();
    }

    private Task SendAsync(string json) =>
        _socket.SendAsync(
            Encoding.UTF8.GetBytes(json + RecordSeparator), WebSocketMessageType.Text, endOfMessage: true,
            TestContext.Current.CancellationToken);

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        var pending = new StringBuilder();
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, _lifetime.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                var text = pending.ToString();
                while (text.Contains(RecordSeparator, StringComparison.Ordinal))
                {
                    var end = text.IndexOf(RecordSeparator, StringComparison.Ordinal);
                    Handle(text[..end]);
                    text = text[(end + 1)..];
                }

                pending.Clear().Append(text);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (WebSocketException)
        {
            // The host closed the connection.
        }
        finally
        {
            _messages.Writer.TryComplete();
        }
    }

    private void Handle(string record)
    {
        using var document = JsonDocument.Parse(record);
        var root = document.RootElement;
        if (!_handshake.Task.IsCompleted)
        {
            if (root.TryGetProperty("error", out var error))
            {
                _handshake.SetException(new InvalidOperationException($"Hub handshake refused: {error.GetString()}"));
            }
            else
            {
                _handshake.SetResult();
            }

            return;
        }

        switch (root.TryGetProperty("type", out var type) ? type.GetInt32() : 0)
        {
            case 1:
                _messages.Writer.TryWrite((
                    root.GetProperty("target").GetString()!,
                    root.GetProperty("arguments").EnumerateArray().Select(a => a.Clone()).ToArray()));
                break;
            case 3:
                if (_completions.TryRemove(root.GetProperty("invocationId").GetString()!, out var completion))
                {
                    completion.SetResult(root.Clone());
                }

                break;
            default:
                break; // pings and close frames
        }
    }
}
