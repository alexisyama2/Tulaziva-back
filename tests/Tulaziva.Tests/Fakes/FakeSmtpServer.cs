using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tulaziva.Tests.Fakes;

/// <summary>
/// A tiny SMTP server for tests: enough of the protocol (EHLO, AUTH LOGIN, MAIL, RCPT, DATA, QUIT) to receive what the .NET client sends,
/// with no TLS. It can refuse the first connections, like a mail server that is busy.
/// </summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private int _sessions;

    public FakeSmtpServer(int refuseFirstSessions = 0)
    {
        RefuseFirstSessions = refuseFirstSessions;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoop);
    }

    public int Port { get; }

    public int RefuseFirstSessions { get; }

    /// <summary>Sessions that were opened, refused ones included.</summary>
    public int Sessions => Volatile.Read(ref _sessions);

    public List<ReceivedMail> Received { get; } = new();

    public sealed record ReceivedMail(string From, string To, string Data, string? User, string? Password);

    private async Task AcceptLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                var number = Interlocked.Increment(ref _sessions);
                _ = Task.Run(() => Serve(client, number));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task Serve(TcpClient client, int number)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };

        try
        {
            if (number <= RefuseFirstSessions)
            {
                await writer.WriteLineAsync("421 4.3.2 Service busy, try later");
                return;
            }

            await writer.WriteLineAsync("220 fake.smtp ready");
            string from = "", to = "";
            string? user = null, password = null;

            while (await reader.ReadLineAsync() is { } line)
            {
                var command = line.ToUpperInvariant();

                if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                {
                    await writer.WriteLineAsync("250-fake.smtp");
                    await writer.WriteLineAsync("250-AUTH LOGIN");
                    await writer.WriteLineAsync("250 8BITMIME");
                }
                else if (command.StartsWith("AUTH LOGIN"))
                {
                    // The client may send the user name together with the command (initial response) or wait for the prompt.
                    var initial = line.Length > "AUTH LOGIN".Length ? line["AUTH LOGIN".Length..].Trim() : "";

                    if (initial.Length > 0)
                    {
                        user = Decode(initial);
                    }
                    else
                    {
                        await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                        user = Decode(await reader.ReadLineAsync());
                    }

                    await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                    password = Decode(await reader.ReadLineAsync());
                    await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                }
                else if (command.StartsWith("MAIL FROM"))
                {
                    from = line["MAIL FROM:".Length..].Trim();
                    await writer.WriteLineAsync("250 OK");
                }
                else if (command.StartsWith("RCPT TO"))
                {
                    to = line["RCPT TO:".Length..].Trim();
                    await writer.WriteLineAsync("250 OK");
                }
                else if (command == "DATA")
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var data = new StringBuilder();

                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                        data.AppendLine(dataLine.StartsWith("..") ? dataLine[1..] : dataLine);

                    lock (Received)
                        Received.Add(new ReceivedMail(from, to, data.ToString(), user, password));

                    await writer.WriteLineAsync("250 2.0.0 Queued");
                }
                else if (command == "QUIT")
                {
                    await writer.WriteLineAsync("221 Bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }
        catch (IOException)
        {
        }
    }

    private static string? Decode(string? base64) => base64 == null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
