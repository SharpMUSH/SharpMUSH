using System.Text.Json;
using Bunit;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The play terminal's longer log: off until the player turns it on, the server's lines only, everything
/// deleted when it is turned off, and earlier lines that do not repeat the ones already shown.
/// </summary>
public class TerminalLogTests : BunitContext
{
	private static readonly TerminalIdentity Ada = new("ada@example.com", "#7:1700000000000");
	private static readonly DateTime Noon = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero).LocalDateTime;

	public TerminalLogTests() => JSInterop.Mode = JSRuntimeMode.Loose;

	private static TerminalLine Server(string text, DateTime at) => new(at, text, text, TerminalLineSource.Server);

	private static string Stored(params TerminalLine[] lines) =>
		JsonSerializer.Serialize(lines.Select(l => new { t = l.Text, h = l.Html, at = new DateTimeOffset(l.Timestamp).ToUnixTimeMilliseconds() }));

	private int Appends => JSInterop.Invocations.Count(i => i.Identifier == "SharpMUSH.TerminalLog.append");

	[Test]
	public async Task Off_KeepsNothing()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);

		await log.AppendAsync(Ada, Server("You say, \"hi\"", Noon));

		await Assert.That(log.On).IsFalse();
		await Assert.That(Appends).IsEqualTo(0);
	}

	[Test]
	public async Task On_KeepsTheServersLines_UnderTheCharacter()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);
		await log.SetAsync(true);

		await log.AppendAsync(Ada, Server("You say, \"hi\"", Noon));
		await log.AppendAsync(Ada, new TerminalLine(Noon, "connect Ada hunter2", TerminalLineSource.Client));

		await Assert.That(Appends).IsEqualTo(1).Because("what the player typed can carry a password");
		var append = JSInterop.Invocations.Single(i => i.Identifier == "SharpMUSH.TerminalLog.append").Arguments;
		await Assert.That((string?)append[0]).IsEqualTo(TerminalLog.KeyFor(Ada));
		await Assert.That((string?)append[2]).Contains("You say");
		var stored = JSInterop.Invocations.Last(i => i.Identifier == "localStorage.setItem").Arguments;
		await Assert.That((string?)stored[0]).IsEqualTo("terminal.keeplogs");
	}

	[Test]
	public async Task TurningItOff_DeletesWhatWasKept()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);
		await log.SetAsync(true);

		await log.SetAsync(false);

		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "SharpMUSH.TerminalLog.clearAll")).IsTrue();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "localStorage.removeItem")).IsTrue();
	}

	[Test]
	public async Task EarlierLines_LeaveOutTheOnesShownFromTheSameMillisecond()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);
		await log.SetAsync(true);
		var before = Server("before", Noon.AddSeconds(-1));
		var a = Server("a", Noon);
		var b = Server("b", Noon);
		var c = Server("c", Noon);
		// The screen starts part way through one millisecond's burst: b and c are shown, a is not.
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.earlier", _ => true).SetResult(Stored(before, a, b, c));

		var earlier = await log.EarlierAsync(Ada, [new TerminalLine(Noon, "Connected", TerminalLineSource.System), b, c, Server("d", Noon.AddSeconds(1))]);

		await Assert.That(earlier.Select(l => l.Text)).IsEquivalentTo(["before", "a"]);
		var asked = JSInterop.Invocations.Single(i => i.Identifier == "SharpMUSH.TerminalLog.earlier").Arguments;
		await Assert.That((long)asked[1]!).IsEqualTo(new DateTimeOffset(Noon).ToUnixTimeMilliseconds());
		await Assert.That((int)asked[2]!).IsEqualTo(TerminalLog.Page + 2);
	}
}
