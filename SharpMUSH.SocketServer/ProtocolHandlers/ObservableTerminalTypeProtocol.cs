using TelnetNegotiationCore.Protocols;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// <see cref="TerminalTypeProtocol"/> with callbacks for observable state changes.
/// <para>
/// TelnetNegotiationCore collects the RFC 1091 / MTTS terminal types a client reports and exposes
/// them on <see cref="TerminalTypeProtocol.TerminalTypes"/>, but offers no event for when they
/// arrive — the library's other MUD protocols (GMCP, NAWS, MSDP, MXP) all have one, TTYPE does not,
/// and <c>IsNegotiated</c> answers whether the option was agreed, not what came back over it. The
/// ConnectionServer needs a push, because the metadata that <c>terminfo()</c> reads lives in the
/// main process and is only reachable over the message bus.
/// </para>
/// <para>
/// TelnetNegotiationCore 4 uses a generated state machine and no longer exposes its former
/// Stateless configuration seam. The connection read loop calls
/// <see cref="PublishTerminalTypesIfChangedAsync"/> after each input segment has been interpreted,
/// so the callback sees the public snapshot only after the protocol has updated it.
/// </para>
/// </summary>
/// <param name="onTerminalTypes">Invoked with the latest terminal-type snapshot after it changes.</param>
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
	private IReadOnlyList<string> _publishedTerminalTypes = [];

	/// <inheritdoc />
	protected override async ValueTask OnNegotiationChangedAsync(bool isNegotiated)
	{
		await base.OnNegotiationChangedAsync(isNegotiated);

		if (onNegotiationChanged is not null)
		{
			await onNegotiationChanged(isNegotiated);
		}
	}

	/// <summary>Publishes the current terminal-type snapshot when it changed during input processing.</summary>
	public async ValueTask PublishTerminalTypesIfChangedAsync()
	{
		if (_publishedTerminalTypes.SequenceEqual(TerminalTypes))
		{
			return;
		}

		_publishedTerminalTypes = [.. TerminalTypes];
		await _onTerminalTypes(_publishedTerminalTypes);
	}
}
