using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2b: the single service the engine calls at its extension seams to consult registered plugin
/// hooks (<see cref="ICommandInterceptor"/>, <see cref="IObjectLifecycleHook"/>). It reads the buckets the
/// <c>PluginCatalog</c> collected and isolates every plugin call in try/catch so a misbehaving hook can
/// never abort dispatch. When no hooks are registered, every method is a cheap no-op — the seams must not
/// change normal dispatch.
///
/// <para>Connection hooks are not routed through this dispatcher: they are wired directly as
/// <c>IConnectionService.ListenState</c> listeners at boot (see the host's plugin bootstrap), so they ride
/// the engine's existing connection-state mechanism.</para>
/// </summary>
public interface IPluginHookDispatcher
{
	/// <summary>True when at least one <see cref="ICommandInterceptor"/> is registered. Lets the command
	/// seam skip all interceptor work entirely in the common no-plugin case.</summary>
	bool HasCommandInterceptors { get; }

	/// <summary>
	/// Run every interceptor's <see cref="ICommandInterceptor.BeforeAsync"/> in load order. Returns
	/// <c>false</c> as soon as any interceptor vetoes (so the caller skips the body, the override seam, and
	/// the remaining before-callbacks); otherwise <c>true</c>.
	/// </summary>
	ValueTask<bool> CommandBeforeAsync(IMUSHCodeParser parser, string command);

	/// <summary>
	/// Run interceptors' <see cref="ICommandInterceptor.TryOverrideAsync"/> in load order. Returns the
	/// first non-<c>null</c> override (short-circuiting the rest), or <c>null</c> when none override.
	/// </summary>
	ValueTask<Option<CallState>?> CommandTryOverrideAsync(IMUSHCodeParser parser, string command);

	/// <summary>Run every interceptor's <see cref="ICommandInterceptor.AfterAsync"/> in load order. Results discarded.</summary>
	ValueTask CommandAfterAsync(IMUSHCodeParser parser, string command);

	/// <summary>Invoke every <see cref="IObjectLifecycleHook.OnCreatedAsync"/>.</summary>
	ValueTask ObjectCreatedAsync(DBRef obj, DBRef creator);

	/// <summary>Invoke every <see cref="IObjectLifecycleHook.OnDestroyingAsync"/> (object still in DB).</summary>
	ValueTask ObjectDestroyingAsync(DBRef obj);
}
