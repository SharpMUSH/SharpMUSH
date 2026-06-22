using Microsoft.AspNetCore.SignalR;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Server-side <see cref="IPluginChangeNotifier"/>: broadcasts the generic <c>ReceivePluginsChanged</c>
/// signal to every connected portal client over the GameHub, mirroring the static <c>SendToXAsync</c>
/// helpers on <see cref="GameHub"/>. The client reacts by forcing a hard browser refresh — the only way to
/// reclaim a compiled component assembly the WASM runtime may have loaded (Mono-WASM cannot unload an ALC).
/// </summary>
public sealed class SignalRPluginChangeNotifier(IHubContext<GameHub, IGameHubClient> hubContext)
	: IPluginChangeNotifier
{
	/// <inheritdoc />
	public Task NotifyPluginsChangedAsync() => GameHub.BroadcastPluginsChangedAsync(hubContext);
}
