using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MarkupString;
using SharpMUSH.Library.Markup;
using SharpMUSH.SocketServer.Models;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// Tells every socket that the engine is restarting, and that it is ready again. Lifecycle events arrive on
/// separate NATS subjects, so they are ordered here before any socket hears one.
/// </summary>
/// <remarks>
/// A notice is a <c>[GAME]</c> notice (<see cref="NoticeMarkup"/>), as the engine's own notices are, and goes
/// out the way the engine's output does: a browser gets a markup frame, a terminal its rendering. While the
/// rendering worker cannot answer, a terminal is sent the plain text instead (<see cref="Broadcast"/>).
/// </remarks>
public sealed class EngineLifecycleNoticeService(
	IConnectionServerService connections,
	IMarkupOutputRenderer renderer,
	IOutputTransformService transform,
	ILogger<EngineLifecycleNoticeService> logger)
{
	/// <summary>The badge the notices carry.</summary>
	public const string Source = "GAME";

	public const string Restarting = "The game is restarting. Your connection will remain open; please wait.";
	public const string Ready = "The game is ready. You may continue.";

	private const string WebSocketConnectionType = "websocket";

	/// <summary>How long one socket's write may take before the next notice no longer waits for it.</summary>
	private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);

	/// <summary>How long the rendering worker is given before a terminal is sent the plain text.</summary>
	private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(1);

	private readonly SemaphoreSlim _transition = new(1, 1);
	private readonly ConditionalWeakTable<Func<byte[], ValueTask>, SemaphoreSlim> _writes = new();
	private DateTimeOffset _lastEvent = DateTimeOffset.MinValue;

	/// <summary>The notice for a restart (<paramref name="ready"/> false) or for the game being back.</summary>
	public static MarkupText Notice(bool ready) => NoticeMarkup.Message(Source, ready ? Ready : Restarting);

	public async Task NotifyAsync(DateTimeOffset timestamp, bool ready, CancellationToken ct = default)
	{
		await _transition.WaitAsync(ct);
		try
		{
			if (timestamp <= _lastEvent) return;
			_lastEvent = timestamp;
			var broadcast = new Broadcast(renderer, transform, logger, Notice(ready), ct);
			await Parallel.ForEachAsync(connections.GetAll(), new ParallelOptions
			{
				MaxDegreeOfParallelism = 16,
				CancellationToken = ct
			}, async (connection, token) =>
			{
				var gate = _writes.GetValue(connection.OutputFunction, _ => new SemaphoreSlim(1, 1));
				if (!await gate.WaitAsync(0, token)) return;
				async Task SendAsync()
				{
					try { await connection.OutputFunction(await broadcast.WireFormAsync(connection)); }
					finally { gate.Release(); }
				}
				try
				{
					await SendAsync().WaitAsync(SendTimeout, token);
				}
				catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
				{
					logger.LogWarning(ex, "Could not send engine lifecycle notice to {Handle}", connection.Handle);
				}
			});
		}
		finally
		{
			_transition.Release();
		}
	}

	/// <summary>
	/// One notice on its way to every socket. Connections that read alike are sent the same bytes, so the
	/// rendering worker is asked once for each kind of client rather than once for each socket, and once it has
	/// failed to answer, the rest are sent the plain text without asking it again.
	/// </summary>
	private sealed class Broadcast(IMarkupOutputRenderer renderer, IOutputTransformService transform, ILogger logger,
		MarkupText notice, CancellationToken ct)
	{
		private readonly string _markup = MarkupTextSerializer.Serialize(notice);
		private readonly ConcurrentDictionary<(ProtocolCapabilities, PlayerOutputPreferences?), Lazy<Task<byte[]>>> _rendered = new();
		private volatile bool _workerDown;

		/// <summary>
		/// What <paramref name="connection"/> is sent: the markup frame for a browser, otherwise the rendering for
		/// its client. Each write ends its own line, so the notice carries no line breaks of its own.
		/// </summary>
		public async ValueTask<byte[]> WireFormAsync(ConnectionServerService.ConnectionData connection)
		{
			if (connection.ConnectionType == WebSocketConnectionType) return WebSocketMarkupFrames.Line(_markup);
			if (_workerDown) return Plain(connection);
			return await _rendered.GetOrAdd((connection.Capabilities, connection.Preferences),
				_ => new Lazy<Task<byte[]>>(() => RenderAsync(connection))).Value;
		}

		private async Task<byte[]> RenderAsync(ConnectionServerService.ConnectionData connection)
		{
			using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
			budget.CancelAfter(RenderTimeout);
			try
			{
				var rendered = await renderer.RenderAsync(_markup, connection, ct: budget.Token);
				return rendered.ApplyOutputTransform
					? await transform.TransformAsync(rendered.Data, connection.Capabilities, budget.Token)
					: rendered.Data;
			}
			catch (Exception ex) when (!ct.IsCancellationRequested)
			{
				_workerDown = true;
				logger.LogWarning(ex, "Rendering worker did not answer; sending the lifecycle notice as plain text");
				return Plain(connection);
			}
		}

		/// <summary>
		/// The notice as the worker would write it with no styling: as a screen reader is sent it to one, and with
		/// the line break a Pueblo client needs to keep it off the next message's line.
		/// </summary>
		private byte[] Plain(ConnectionServerService.ConnectionData connection)
		{
			var text = NoticeMarkup.ForTelnet(notice, connection.Capabilities.ReadsAloud).ToPlainText();
			if (connection.Capabilities.Format == OutputFormat.Pueblo) text += "<BR>";
			return connection.EncodingFunction().GetBytes(text);
		}
	}
}
