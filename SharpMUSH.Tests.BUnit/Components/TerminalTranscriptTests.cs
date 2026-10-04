using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The terminal's own view of its lines, and what it reads from typed input and writes as control
/// frames, without rendering <see cref="GlobalTerminal"/>.
/// </summary>
public class TerminalTranscriptTests
{
	private static TerminalLine Line(string text) => new(DateTime.UnixEpoch, text, TerminalLineSource.Server);

	[Test]
	public async Task StartsWithTheServicesLines_AndAppends()
	{
		var transcript = new TerminalTranscript([Line("one"), Line("two")]);

		transcript.Add(Line("three"));

		await Assert.That(transcript.Lines.Select(l => l.Text)).IsEquivalentTo(["one", "two", "three"]);
	}

	[Test]
	public async Task DropsTheOldestLine_OnceFull()
	{
		var transcript = new TerminalTranscript(Enumerable.Range(0, TerminalService.MaxLines).Select(i => Line($"line {i}")));

		transcript.Add(Line("newest"));

		await Assert.That(transcript.Lines.Count).IsEqualTo(TerminalService.MaxLines);
		await Assert.That(transcript.Lines[0].Text).IsEqualTo("line 1");
		await Assert.That(transcript.Lines[^1].Text).IsEqualTo("newest");
	}

	[Test]
	public async Task Clear_EmptiesOnlyTheTranscript()
	{
		var initial = new List<TerminalLine> { Line("kept by the service") };
		var transcript = new TerminalTranscript(initial);

		transcript.Clear();

		await Assert.That(transcript.Lines).IsEmpty();
		await Assert.That(initial).HasCount(1);
	}

	[Test]
	[Arguments("connect Ada secret", "Ada")]
	[Arguments("  CONNECT   Bob pass  ", "Bob")]
	[Arguments("connect Carol", "Carol")]
	public async Task TypedConnectName_IsTheSecondWord(string input, string expected)
		=> await Assert.That(TerminalInput.TypedConnectName(input) is string name && name == expected).IsTrue();

	[Test]
	[Arguments("connect token abc123")]
	[Arguments("connectAda secret")]
	[Arguments("say connect Ada")]
	[Arguments("connect ")]
	public async Task TypedConnectName_NamesNoOne(string input)
		=> await Assert.That(TerminalInput.TypedConnectName(input) is NotFound).IsTrue();

	[Test]
	public async Task NawsFrame_CarriesTheGrid()
		=> await Assert.That(TerminalInput.NawsFrame(80, 24)).IsEqualTo("{\"type\":\"naws\",\"cols\":80,\"rows\":24}");
}
