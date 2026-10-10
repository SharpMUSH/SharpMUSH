using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// <see cref="TerminalResumeStore"/>: one resume point per terminal and identity, and a clear that
/// stops every connection opened before it from writing one back.
/// </summary>
public class TerminalResumeStoreTests
{
	private static readonly TerminalIdentity Alice = new("alice", "#5:100");
	private static readonly TerminalIdentity Bob = new("bob", "#6:200");

	[Test]
	public async Task Identity_of_a_character_is_its_objid_under_the_account()
	{
		var character = new AccountAuthService.CharacterSummary(5, 100, "Alice", "");

		await Assert.That(TerminalIdentity.Of("alice", character)).IsEqualTo((TerminalIdentity?)Alice);
		await Assert.That(TerminalIdentity.Of(null, character)).IsNull();
	}

	[Test]
	public async Task Keys_differ_by_terminal_and_by_identity()
	{
		string[] keys =
		[
			TerminalResumeStore.KeyFor("play", Alice),
			TerminalResumeStore.KeyFor("portal", Alice),
			TerminalResumeStore.KeyFor("play", Bob),
		];

		await Assert.That(keys.Distinct().Count()).IsEqualTo(3);
		await Assert.That(keys.All(k => k.StartsWith(TerminalResumeStore.KeyPrefix, StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task Open_reads_the_point_stored_for_that_terminal_and_identity()
	{
		var js = new FakeResumeJs();
		js.Seed(TerminalResumeStore.KeyFor("play", Alice), "{\"token\":\"tok-1\",\"lastSeq\":42}");
		var store = new TerminalResumeStore(js);

		var alice = await store.OpenAsync("play", Alice);
		var bob = await store.OpenAsync("play", Bob);
		var alicePortal = await store.OpenAsync("portal", Alice);

		await Assert.That(alice.Stored.Expect<TerminalResumePoint>()).IsEqualTo(new TerminalResumePoint("tok-1", 42));
		await Assert.That(bob.Stored is NotFound).IsTrue();
		await Assert.That(alicePortal.Stored is NotFound).IsTrue();
	}

	[Test]
	[Arguments("not json")]
	[Arguments("{\"token\":\"\",\"lastSeq\":3}")]
	[Arguments("{\"token\":\"t\",\"lastSeq\":-1}")]
	[Arguments("{\"lastSeq\":3}")]
	public async Task A_damaged_stored_point_is_no_point(string stored)
	{
		var js = new FakeResumeJs();
		js.Seed(TerminalResumeStore.KeyFor("play", Alice), stored);

		var slot = await new TerminalResumeStore(js).OpenAsync("play", Alice);

		await Assert.That(slot.Stored is NotFound).IsTrue();
	}

	[Test]
	public async Task Save_writes_now_and_stage_leaves_the_write_to_the_page()
	{
		var js = new FakeResumeJs();
		var store = new TerminalResumeStore(js);
		var slot = await store.OpenAsync("play", Alice);
		var key = TerminalResumeStore.KeyFor("play", Alice);

		await slot.SaveAsync(new TerminalResumePoint("tok-1", 0));
		await slot.StageAsync(new TerminalResumePoint("tok-1", 1));
		await slot.StageAsync(new TerminalResumePoint("tok-1", 2));

		await Assert.That(js.Writes).IsEqualTo(1);
		await Assert.That(js.StoredValue(key)).IsEqualTo("{\"token\":\"tok-1\",\"lastSeq\":0}");
		await Assert.That(js.StagedValue(key)).IsEqualTo("{\"token\":\"tok-1\",\"lastSeq\":2}");
	}

	[Test]
	public async Task Clear_all_forgets_every_point_and_revokes_the_slots_opened_before_it()
	{
		var js = new FakeResumeJs();
		var store = new TerminalResumeStore(js);
		var before = await store.OpenAsync("play", Alice);
		await before.SaveAsync(new TerminalResumePoint("tok-1", 3));
		js.Seed(TerminalResumeStore.KeyFor("portal", Bob), "{\"token\":\"tok-b\",\"lastSeq\":1}");

		await store.ClearAllAsync();
		await before.SaveAsync(new TerminalResumePoint("tok-2", 4));
		await before.StageAsync(new TerminalResumePoint("tok-2", 5));
		js.Flush();

		await Assert.That(before.Revoked).IsTrue();
		await Assert.That(js.StoredValue(TerminalResumeStore.KeyFor("play", Alice))).IsNull();
		await Assert.That(js.StoredValue(TerminalResumeStore.KeyFor("portal", Bob))).IsNull();

		// A connection opened after the clear keeps its point as usual.
		var after = await store.OpenAsync("play", Alice);
		await after.SaveAsync(new TerminalResumePoint("tok-3", 0));
		await Assert.That(after.Revoked).IsFalse();
		await Assert.That(js.StoredValue(TerminalResumeStore.KeyFor("play", Alice))).IsEqualTo("{\"token\":\"tok-3\",\"lastSeq\":0}");
	}

	[Test]
	public async Task The_prompt_is_kept_beside_the_point_and_forgotten_with_it()
	{
		var js = new FakeResumeJs();
		var store = new TerminalResumeStore(js);
		var slot = await store.OpenAsync("play", Alice);
		var promptKey = TerminalResumeStore.PromptKeyFor("play", Alice);
		var prompt = new SharpMUSH.Client.Models.TerminalPrompt(
			new SharpMUSH.Client.Models.TerminalLine(DateTime.Now, "Read which post?", SharpMUSH.Client.Models.TerminalLineSource.Server), "s1");

		await slot.SaveAsync(new TerminalResumePoint("tok-1", 0));
		await slot.KeepPromptAsync(prompt);
		await Assert.That(js.StagedValue(promptKey)).IsNotNull().Because("written on the page's timer, like the lines");
		js.Flush();

		var reopened = await store.OpenAsync("play", Alice);
		await Assert.That(reopened.TakePrompt().Expect<SharpMUSH.Client.Models.TerminalPrompt>().Line.Text).IsEqualTo("Read which post?");
		await Assert.That(reopened.TakePrompt() is NotFound).IsTrue().Because("handed out once");

		await slot.KeepPromptAsync(null);
		await Assert.That(js.StoredValue(promptKey)).IsNull();

		await slot.KeepPromptAsync(prompt);
		js.Flush();
		await slot.ClearAsync();
		await Assert.That(js.StoredValue(promptKey)).IsNull();
	}
}
