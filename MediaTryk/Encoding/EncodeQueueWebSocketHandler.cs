using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace MediaTryk.Encoding;

/// <summary>
/// Streams encode queue updates over a WebSocket: a snapshot of all known jobs
/// on connect, then one EncodeJobDto message per state change until either side disconnects.
/// </summary>
public static class EncodeQueueWebSocketHandler
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    public static async Task RunAsync(WebSocket socket, EncodeQueue queue, CancellationToken cancellationToken)
    {
        // Subscribe before taking the snapshot so no update can fall in between;
        // a job appearing in both just means the client applies it twice.
        var (subscriptionId, reader) = queue.Subscribe();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var closeTask = WaitForCloseAsync(socket, cts.Token);
            var sendTask = SendLoopAsync(socket, queue, reader, cts.Token);

            await Task.WhenAny(closeTask, sendTask);
            await cts.CancelAsync();
            try
            {
                await Task.WhenAll(closeTask, sendTask);
            }
            catch (OperationCanceledException)
            {
            }

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // Client dropped the connection; nothing left to do.
        }
        finally
        {
            queue.Unsubscribe(subscriptionId);
        }
    }

    private static async Task SendLoopAsync(
        WebSocket socket,
        EncodeQueue queue,
        ChannelReader<EncodeJob> reader,
        CancellationToken cancellationToken)
    {
        foreach (var job in queue.GetAll())
        {
            await SendAsync(socket, job, cancellationToken);
        }

        await foreach (var job in reader.ReadAllAsync(cancellationToken))
        {
            await SendAsync(socket, job, cancellationToken);
        }
    }

    private static Task SendAsync(WebSocket socket, EncodeJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(job.ToDto(), JsonOptions);
        return socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private static async Task WaitForCloseAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
        }
    }
}