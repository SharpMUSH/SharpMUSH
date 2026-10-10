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
	public async Task DropsTheOldestChunk_OnceTheRestHoldAFullBuffer()
	{
		var transcript = new TerminalTranscript(Enumerable.Range(0, TerminalService.MaxLines).Select(i => Line($"line {i}")));

		transcript.Add(Line("newest"));
		var lines = transcript.Lines.ToList();

		await Assert.That(lines.Count).IsEqualTo(TerminalService.MaxLines + 1).Because("the oldest chunk still has lines the rest do not cover");
		await Assert.That(lines[^1].Text).IsEqualTo("newest");

		for (var i = 1; i < TerminalChunk.Size; i++)
			transcript.Add(Line($"more {i}"));
		lines = [.. transcript.Lines];

		await Assert.That(lines.Count).IsEqualTo(TerminalService.MaxLines);
		await Assert.That(lines[0].Text).IsEqualTo($"line {TerminalChunk.Size}");
	}

	[Test]
	public async Task OnlyTheNewestChunkChanges_WhenALineArrives()
	{
		var transcript = new TerminalTranscript(Enumerable.Range(0, TerminalChunk.Size + 5).Select(i => Line($"line {i}")));
		var first = transcript.Live[0];
		var firstVersion = first.Version;
		var lastVersion = transcript.Live[^1].Version;

		transcript.Add(Line("newest"));

		await Assert.That(transcript.Live).HasCount(2);
		await Assert.That(first.Version).IsEqualTo(firstVersion);
		await Assert.That(transcript.Live[^1].Version).IsEqualTo(lastVersion + 1);
	}

	[Test]
	public async Task EarlierLines_GoAboveTheRest_AndTakeTheChunksTheBufferDrops()
	{
		var transcript = new TerminalTranscript(Enumerable.Range(0, TerminalService.MaxLines).Select(i => Line($"line {i}")));
		transcript.Prepend([Line("earlier 0"), Line("earlier 1")]);

		await Assert.That(transcript.Oldest!.Text).IsEqualTo("earlier 0");

		for (var i = 0; i < TerminalChunk.Size; i++)
			transcript.Add(Line($"new {i}"));
		var lines = transcript.Lines.Select(l => l.Text).ToList();

		await Assert.That(lines.Take(3)).IsEquivalentTo(["earlier 0", "earlier 1", "line 0"]).Because("no gap opens between the earlier lines and the rest");
		await Assert.That(lines.Count).IsEqualTo(TerminalService.MaxLines + TerminalChunk.Size + 2);
	}

	[Test]
	public async Task EarlierLines_AreBounded()
	{
		var transcript = new TerminalTranscript([Line("now")]);
		transcript.Prepend([.. Enumerable.Range(0, TerminalTranscript.MaxEarlierLines).Select(i => Line($"earlier {i}"))]);

		await Assert.That(transcript.CanTakeEarlier).IsFalse();
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
