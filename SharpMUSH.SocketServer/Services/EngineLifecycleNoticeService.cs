using System.Runtime.CompilerServices;
using MarkupString;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// Tells every socket that the engine is restarting, and that it is ready again. Lifecycle events arrive on
/// separate NATS subjects, so they are ordered here before any socket hears one.
/// </summary>
/// <remarks>
/// A notice is a <c>[GAME]</c> notice (<see cref="NoticeMarkup"/>), as the engine's own notices are, and goes
/// out the way the engine's output does: a browser gets a markup frame, a terminal its rendering. While the
/// rendering worker cannot answer, a terminal is sent the plain text instead.
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
			var notice = Notice(ready);
			var markup = MarkupTextSerializer.Serialize(notice);
			var plain = notice.ToPlainText();
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
					try { await connection.OutputFunction(await WireFormAsync(connection, markup, plain, token)); }
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
	/// What <paramref name="connection"/> is sent: the markup frame for a browser, otherwise the rendering for its
	/// client. Each write ends its own line, so the notice carries no line breaks of its own.
	/// </summary>
	private async ValueTask<byte[]> WireFormAsync(ConnectionServerService.ConnectionData connection, string markup,
		string plain, CancellationToken ct)
	{
		if (connection.ConnectionType == WebSocketConnectionType) return WebSocketMarkupFrames.Line(markup);

		using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
		budget.CancelAfter(RenderTimeout);
		try
		{
			var rendered = await renderer.RenderAsync(markup, connection, ct: budget.Token);
			return rendered.ApplyOutputTransform
				? await transform.TransformAsync(rendered.Data, connection.Capabilities, budget.Token)
				: rendered.Data;
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			logger.LogDebug(ex, "Rendering worker did not answer for {Handle}; sending the lifecycle notice as plain text",
				connection.Handle);
			return connection.EncodingFunction().GetBytes(plain);
		}
	}
}
