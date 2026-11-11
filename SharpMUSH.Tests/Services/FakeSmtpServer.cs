using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// An SMTP server on a loopback port that takes every email and keeps its DATA. It offers no extensions, so a client
/// sends in plain text and without signing in.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _accepting;

	/// <summary>Each email received: its recipients and its DATA as sent.</summary>
	public ConcurrentQueue<(IReadOnlyList<string> To, string Data)> Received { get; } = new();

	public FakeSmtpServer()
	{
		_listener.Start();
		_accepting = AcceptAsync();
	}

	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	/// <summary>The first email whose DATA contains <paramref name="text"/>, waiting up to ten seconds for it.</summary>
	public async Task<string?> WaitForAsync(string text)
	{
		for (var waited = 0; waited < 200; waited++)
		{
			if (Received.FirstOrDefault(mail => mail.Data.Contains(text, StringComparison.Ordinal)) is { Data: { } data })
				return data;
			await Task.Delay(50);
		}

		return null;
	}

	private async Task AcceptAsync()
	{
		while (!_stop.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(_stop.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			_ = ServeAsync(client);
		}
	}

	private async Task ServeAsync(TcpClient client)
	{
		using var _ = client;
		var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.ASCII);
		await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
		var to = new List<string>();
		await writer.WriteLineAsync("220 fake ready");
		while (await reader.ReadLineAsync(_stop.Token) is { } line)
		{
			var verb = line.Split(' ', 2)[0].ToUpperInvariant();
			switch (verb)
			{
				case "EHLO" or "HELO":
					await writer.WriteLineAsync("250 fake");
					break;
				case "RCPT":
					to.Add(line);
					await writer.WriteLineAsync("250 ok");
					break;
				case "DATA":
					await writer.WriteLineAsync("354 go on");
					var data = new StringBuilder();
					while (await reader.ReadLineAsync(_stop.Token) is { } body && body != ".")
						data.AppendLine(body);
					Received.Enqueue((to.ToArray(), data.ToString()));
					to.Clear();
					await writer.WriteLineAsync("250 queued");
					break;
				case "QUIT":
					await writer.WriteLineAsync("221 bye");
					return;
				default:
					await writer.WriteLineAsync("250 ok");
					break;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _stop.CancelAsync();
		_listener.Stop();
		try
		{
			await _accepting;
		}
		catch (ObjectDisposedException)
		{
			// Stopping the listener can end a pending accept this way; the server is stopping either way.
		}

		_stop.Dispose();
	}
}
