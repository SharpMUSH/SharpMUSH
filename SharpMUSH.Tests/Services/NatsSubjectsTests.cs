using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The subject a message type maps to is shared by the publishing and consuming processes, so its
/// spelling is pinned: the <c>Message</c> suffix goes, and the rest is kebab-cased with an acronym
/// kept as one word.
/// </summary>
public class NatsSubjectsTests
{
	public sealed record TelnetInputMessage;
	public sealed record MSDPUpdateMessage;
	public sealed record MainProcessReadyMessage;
	public sealed record Heartbeat;

	[Test]
	[Arguments(typeof(TelnetInputMessage), "telnet-input")]
	[Arguments(typeof(MSDPUpdateMessage), "msdp-update")]
	[Arguments(typeof(MainProcessReadyMessage), "main-process-ready")]
	[Arguments(typeof(Heartbeat), "heartbeat")]
	public async Task KebabName_DropsTheSuffixAndKebabCasesTheRest(Type messageType, string expected)
		=> await Assert.That(NatsSubjects.KebabName(messageType)).IsEqualTo(expected);

	[Test]
	public async Task For_PutsTheKebabNameUnderThePrefix()
		=> await Assert.That(NatsSubjects.For(typeof(TelnetInputMessage), "sharpmush.input")).IsEqualTo("sharpmush.input.telnet-input");
}
