using Stateless;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using TelnetNegotiationCore.Plugins;
using TelnetNegotiationCore.Protocols;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// <see cref="TerminalTypeProtocol"/> with a completion callback.
/// <para>
/// TelnetNegotiationCore collects the RFC 1091 / MTTS terminal types a client reports and exposes
/// them on <see cref="TerminalTypeProtocol.TerminalTypes"/>, but offers no event for when they
/// arrive — the library's other MUD protocols (GMCP, NAWS, MSDP, MXP) all have one, TTYPE does not,
/// and <c>IsNegotiated</c> answers whether the option was agreed, not what came back over it. The
/// ConnectionServer needs a push, because the metadata that <c>terminfo()</c> reads lives in the
/// main process and is only reachable over the message bus.
/// </para>
/// <para>
/// Stateless runs entry actions in the order they were registered, so registering ours after
/// <see cref="TerminalTypeProtocol.ConfigureStateMachine"/> has run means the base has already
/// recorded the value — and, when the list is not finished, has already asked for the next one. The
/// callback therefore fires once per reported type with the list so far, and the last call is the
/// complete one; the receiver treats each as a snapshot rather than an increment.
/// </para>
/// <para>
/// Client mode is left alone: there the states belong to answering a server's request, not to
/// learning anything about the peer.
/// </para>
/// </summary>
/// <param name="onTerminalTypes">Invoked with the types reported so far, each time one arrives.</param>
/// <param name="onNegotiationChanged">
/// Invoked when the peer agrees to or refuses TTYPE, ahead of any type actually arriving. TTYPE is
/// usually the first option a client answers, so this is the earliest honest evidence that it speaks
/// telnet at all — including for a client that agrees and then names nothing.
/// </param>
public sealed class ObservableTerminalTypeProtocol(
	Func<IReadOnlyList<string>, ValueTask> onTerminalTypes,
	Func<bool, ValueTask>? onNegotiationChanged = null) : TerminalTypeProtocol
{
	private readonly Func<IReadOnlyList<string>, ValueTask> _onTerminalTypes =
		onTerminalTypes ?? throw new ArgumentNullException(nameof(onTerminalTypes));

	/// <inheritdoc />
	protected override async ValueTask OnNegotiationChangedAsync(bool isNegotiated)
	{
		await base.OnNegotiationChangedAsync(isNegotiated);

		if (onNegotiationChanged is not null)
		{
			await onNegotiationChanged(isNegotiated);
		}
	}

	/// <inheritdoc />
	public override void ConfigureStateMachine(StateMachine<State, Trigger> stateMachine, IProtocolContext context)
	{
		base.ConfigureStateMachine(stateMachine, context);

		if (context.Mode != TelnetInterpreter.TelnetMode.Server)
		{
			return;
		}

		stateMachine.Configure(State.CompletingTerminalType)
			.OnEntryAsync(async () => await _onTerminalTypes(TerminalTypes));
	}
}
