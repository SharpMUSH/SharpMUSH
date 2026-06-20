using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2b engine-extension hook: react to connection lifecycle transitions. A plugin entry type may
/// implement this to observe sockets connecting, players binding a character (login), and disconnecting.
/// The host registers every implementer as an <c>IConnectionService.ListenState</c> listener at boot, so
/// these fire from the same mechanism the engine already uses for connection state changes.
///
/// <para>A plugin implements any subset (the defaults are no-ops). The handle is the connection's session
/// handle; <see cref="OnLoginAsync"/> additionally carries the bound character's <see cref="DBRef"/>.
/// Resolve engine services from the captured root <see cref="IServiceProvider"/> the host passes when it
/// registers the listener.</para>
/// </summary>
public interface IConnectionHook
{
	/// <summary>A raw socket reached the Connected state (pre-login). Default is a no-op.</summary>
	ValueTask OnConnectAsync(long handle) => ValueTask.CompletedTask;

	/// <summary>A connection bound a character (logged in). Default is a no-op.</summary>
	ValueTask OnLoginAsync(long handle, DBRef player) => ValueTask.CompletedTask;

	/// <summary>A connection disconnected. <paramref name="player"/> is the bound character, if any. Default is a no-op.</summary>
	ValueTask OnDisconnectAsync(long handle, DBRef? player) => ValueTask.CompletedTask;
}
