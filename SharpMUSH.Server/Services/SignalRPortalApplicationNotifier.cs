using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Server-side <see cref="IPortalApplicationNotifier"/>: sends <c>ReceiveAppRefresh</c> to the hub group of the
/// application, or of one character's copy of it. A group is told at most once a second; an ask inside that
/// second is sent when it ends, so the last change always reaches the page.
/// </summary>
public sealed class SignalRPortalApplicationNotifier(IHubContext<GameHub, IGameHubClient> hubContext, TimeProvider time)
	: IPortalApplicationNotifier
{
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

	private readonly ConcurrentDictionary<string, DateTimeOffset> _sent = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, byte> _waiting = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public Task RefreshAsync(string slug, string? objid)
	{
		var app = objid is { Length: > 0 } ? $"{slug}:{objid}" : slug;
		var now = time.GetUtcNow();
		var last = _sent.GetOrAdd(app, DateTimeOffset.MinValue);
		if (now - last >= Interval && _sent.TryUpdate(app, now, last))
			return GameHub.SendAppRefreshAsync(hubContext, app);
		if (!_waiting.TryAdd(app, 0)) return Task.CompletedTask;
		_ = SendLaterAsync(app, last + Interval - now);
		return Task.CompletedTask;
	}

	private async Task SendLaterAsync(string app, TimeSpan wait)
	{
		await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, time);
		_waiting.TryRemove(app, out _);
		_sent[app] = time.GetUtcNow();
		await GameHub.SendAppRefreshAsync(hubContext, app);
	}
}
