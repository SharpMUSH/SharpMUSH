using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2b engine-extension hook: the C# analog of softcode <c>@hook</c> on a command. A plugin entry
/// type may implement this to intercept built-in command execution. The engine consults every registered
/// interceptor at the same command seam where it consults the softcode <c>BEFORE</c>/<c>OVERRIDE</c>/
/// <c>AFTER</c> hooks, so C# interceptors compose with — and never replace — softcode hooks.
///
/// <para>A plugin implements any subset of the three callbacks (the defaults are no-ops):</para>
/// <list type="bullet">
/// <item><description><see cref="BeforeAsync"/> runs before the command body. Returning <c>false</c>
/// <b>vetoes</b> the command: the body and any later interceptors are skipped and dispatch short-circuits
/// (mirroring a softcode <c>IGNORE</c> hook that returns false).</description></item>
/// <item><description><see cref="TryOverrideAsync"/> runs before the command body. Returning a
/// non-<c>null</c> <see cref="Option{CallState}"/> <b>short-circuits</b> the built-in: that result is
/// returned in place of the command body (mirroring a softcode <c>OVERRIDE</c> hook).</description></item>
/// <item><description><see cref="AfterAsync"/> runs after the command body completes (or after an
/// override/veto), purely for side effects; its result is discarded (mirroring a softcode <c>AFTER</c>
/// hook).</description></item>
/// </list>
///
/// <para>Interceptors are consulted in plugin load order. The <paramref name="command"/> string is the raw
/// command-with-switches text exactly as the dispatcher saw it. Resolve engine services at call time via
/// <c>parser.ServiceProvider.GetRequiredService&lt;T&gt;()</c>.</para>
/// </summary>
public interface ICommandInterceptor
{
	/// <summary>
	/// Runs before the command body. Return <c>false</c> to veto the command (skip the body, later
	/// interceptors, and the override seam). The default returns <c>true</c> (no veto).
	/// </summary>
	ValueTask<bool> BeforeAsync(IMUSHCodeParser parser, string command) => ValueTask.FromResult(true);

	/// <summary>
	/// Runs before the command body. Return a non-<c>null</c> <see cref="Option{CallState}"/> to
	/// short-circuit the built-in with that result. The default returns <c>null</c> (no override).
	/// </summary>
	ValueTask<Option<CallState>?> TryOverrideAsync(IMUSHCodeParser parser, string command)
		=> ValueTask.FromResult<Option<CallState>?>(null);

	/// <summary>
	/// Runs after the command body (or after a veto/override). Side-effect only; the result is discarded.
	/// The default is a no-op.
	/// </summary>
	ValueTask AfterAsync(IMUSHCodeParser parser, string command) => ValueTask.CompletedTask;
}
