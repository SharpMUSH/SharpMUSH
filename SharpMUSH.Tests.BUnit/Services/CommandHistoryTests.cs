using Bunit;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The command history Up and Down walk: newest last, no passwords, no repeats in a row, capped; a walk that
/// narrows to what was typed and gives it back past the newest entry.
/// </summary>
public class CommandHistoryTests : BunitContext
{
	public CommandHistoryTests() => JSInterop.Mode = JSRuntimeMode.Loose;

	[Test]
	public async Task Commands_AreKeptNewestLast_WithoutPasswordsOrRepeats()
	{
		var history = new CommandHistory(JSInterop.JSRuntime);
		foreach (var line in new[] { "look", "connect Bob hunter2", "say hi", "say hi", "@password old=new", "  ", "who" })
		{
			await history.AddAsync(line);
		}
		await Assert.That(history.Entries).IsEquivalentTo(new[] { "look", "say hi", "who" });
		var stored = JSInterop.Invocations.Last(i => i.Identifier == "localStorage.setItem").Arguments;
		await Assert.That((string?)stored[0]).IsEqualTo("play.history");
		await Assert.That((string?)stored[1]).DoesNotContain("hunter2");
	}

	[Test]
	[Arguments("cd Bob hunter2")]
	[Arguments("cv Bob hunter2")]
	[Arguments("ch Bob hunter2")]
	[Arguments("connect\tBob hunter2")]
	[Arguments("CONNECT Bob hunter2")]
	[Arguments("claim Bob hunter2")]
	[Arguments("@account/claim Bob=hunter2")]
	[Arguments("@ACCOUNT/NEWPASSWORD bob=hunter2")]
	public async Task EveryConnectForm_IsKeptOut(string line)
	{
		await Assert.That(CommandHistory.CarriesSecret(line)).IsTrue();
		var history = new CommandHistory(JSInterop.JSRuntime);
		await history.AddAsync(line);
		await Assert.That(history.Entries).IsEmpty();
	}

	[Test]
	public async Task TheOldest_GoFirst_PastTheCapacity()
	{
		var history = new CommandHistory(JSInterop.JSRuntime);
		for (var i = 0; i < CommandHistory.Capacity + 5; i++) await history.AddAsync($"cmd {i}");
		await Assert.That(history.Entries.Count).IsEqualTo(CommandHistory.Capacity);
		await Assert.That(history.Entries[0]).IsEqualTo("cmd 5");
	}

	[Test]
	public async Task AKeptHistory_IsReadBack()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.history").SetResult("""["look","who"]""");
		var history = new CommandHistory(JSInterop.JSRuntime);
		await history.LoadAsync();
		await Assert.That(history.Entries).IsEquivalentTo(new[] { "look", "who" });
	}

	[Test]
	public async Task Up_GoesBack_Down_ComesForward_AndPastTheNewest_GivesBackTheDraft()
	{
		string[] entries = ["look", "say hi", "who"];
		var walk = new HistoryWalk();
		await Assert.That(walk.Older(entries, "")).IsEqualTo("who");
		await Assert.That(walk.Older(entries, "who")).IsEqualTo("say hi");
		await Assert.That(walk.Older(entries, "say hi")).IsEqualTo("look");
		await Assert.That(walk.Older(entries, "look")).IsNull().Because("the oldest stays");
		await Assert.That(walk.Newer(entries)).IsEqualTo("say hi");
		await Assert.That(walk.Newer(entries)).IsEqualTo("who");
		await Assert.That(walk.Newer(entries)).IsEqualTo("").Because("past the newest is what was being typed");
		await Assert.That(walk.Walking).IsFalse();
	}

	[Test]
	public async Task WhatWasTyped_NarrowsTheWalk()
	{
		string[] entries = ["say one", "look", "say two", "who"];
		var walk = new HistoryWalk();
		await Assert.That(walk.Older(entries, "say")).IsEqualTo("say two");
		await Assert.That(walk.Older(entries, "say two")).IsEqualTo("say one");
		await Assert.That(walk.Newer(entries)).IsEqualTo("say two");
		await Assert.That(walk.Newer(entries)).IsEqualTo("say").Because("the draft comes back");
	}

	[Test]
	public async Task NoMatch_LeavesTheLineAlone()
	{
		var walk = new HistoryWalk();
		await Assert.That(walk.Older(["look"], "page")).IsNull();
		await Assert.That(walk.Walking).IsFalse();
	}
}
