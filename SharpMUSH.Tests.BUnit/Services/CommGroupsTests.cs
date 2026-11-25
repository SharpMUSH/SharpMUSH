using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="CommGroups"/>: the account's channel groups, checked on every change and written whole to the store.
/// </summary>
public class CommGroupsTests
{
	private static async Task<(TestCommGroupStore Store, CommGroups Groups)> CreateAsync(params CommGroup[] stored)
	{
		var store = new TestCommGroupStore { Json = stored.Length == 0 ? null : CommGroup.ToJson(stored) };
		var groups = new CommGroups(store);
		await groups.LoadAsync();
		return (store, groups);
	}

	private static string Refusal(Result<CommGroup> result) =>
		result is Error<string> error ? error.Value : throw new InvalidOperationException("It was saved.");

	[Test]
	public async Task Saving_AddsTheGroup_AndWritesItToTheStore()
	{
		var (store, groups) = await CreateAsync();
		var changes = 0;
		groups.Changed += () => changes++;

		var saved = await groups.SaveAsync(null, "  Roleplay ", ["Public", " Scenes", "public", ""]);

		var group = saved.Expect<CommGroup>();
		await Assert.That(group.Name).IsEqualTo("Roleplay");
		await Assert.That(group.Channels).IsEquivalentTo(new[] { "Public", "Scenes" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		var stored = CommGroup.FromJson(store.Json).Single();
		await Assert.That(stored.Name).IsEqualTo("Roleplay");
		await Assert.That(stored.Channels).IsEquivalentTo(group.Channels, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(changes).IsEqualTo(1);
	}

	[Test]
	public async Task Saving_IsRefused_WithTheReason()
	{
		var (_, groups) = await CreateAsync(new CommGroup("Roleplay", ["Public", "Scenes"]));

		await Assert.That(Refusal(await groups.SaveAsync(null, " ", ["Public", "OOC"]))).IsEqualTo("NavPlayGroupNameMissing");
		await Assert.That(Refusal(await groups.SaveAsync(null, new string('x', CommGroup.NameLimit + 1), ["Public", "OOC"]))).IsEqualTo("NavPlayGroupNameTooLong");
		await Assert.That(Refusal(await groups.SaveAsync(null, "ROLEPLAY", ["Public", "OOC"]))).IsEqualTo("NavPlayGroupNameTaken");
		await Assert.That(Refusal(await groups.SaveAsync(null, "Solo", ["Public"]))).IsEqualTo("NavPlayGroupTooFewChannels");
		await Assert.That(groups.Groups.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Saving_PastTheLimit_IsRefused_ButChangingOneIsNot()
	{
		var full = Enumerable.Range(0, CommGroup.GroupLimit).Select(i => new CommGroup($"G{i}", ["Public", "OOC"])).ToArray();
		var (_, groups) = await CreateAsync(full);

		await Assert.That(Refusal(await groups.SaveAsync(null, "One more", ["Public", "OOC"]))).IsEqualTo("NavPlayGroupTooMany");
		await Assert.That(await groups.SaveAsync("G3", "G3", ["Public", "Scenes"]) is CommGroup).IsTrue();
	}

	[Test]
	public async Task AStoreThatWillNotTakeIt_LeavesTheGroupsAsTheyWere()
	{
		var (store, groups) = await CreateAsync(new CommGroup("Roleplay", ["Public", "Scenes"]));
		store.Refuse = true;

		await Assert.That(Refusal(await groups.SaveAsync(null, "Staff", ["Admin", "Jobs"]))).IsEqualTo("NavPlayGroupNotSaved");
		await Assert.That(await groups.RemoveAsync("Roleplay")).IsFalse();
		await Assert.That(groups.Groups.Select(g => g.Name)).IsEquivalentTo(new[] { "Roleplay" });
	}

	[Test]
	public async Task Renaming_KeepsItsPlace_AndTheOldKeyStillFindsIt()
	{
		var (_, groups) = await CreateAsync(new CommGroup("Roleplay", ["Public", "Scenes"]), new CommGroup("Staff", ["Admin", "Jobs"]));

		await groups.SaveAsync("roleplay", "Story", ["Public", "Scenes", "OOC"]);

		await Assert.That(groups.Groups.Select(g => g.Name)).IsEquivalentTo(new[] { "Story", "Staff" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(groups.Find("group Roleplay").Expect<CommGroup>().Name).IsEqualTo("Story")
			.Because("a view open under the old name keeps its group until it takes the new key");
		await Assert.That(groups.Find("group story").Expect<CommGroup>().Channels.Count).IsEqualTo(3);
	}

	[Test]
	public async Task Removing_DropsIt_AndItsOldNames()
	{
		var (store, groups) = await CreateAsync(new CommGroup("Roleplay", ["Public", "Scenes"]));
		await groups.SaveAsync("Roleplay", "Story", ["Public", "Scenes"]);

		await Assert.That(await groups.RemoveAsync("Story")).IsTrue();

		await Assert.That(groups.Groups).IsEmpty();
		await Assert.That(CommGroup.FromJson(store.Json)).IsEmpty();
		await Assert.That(groups.Find("group Roleplay") is NotFound).IsTrue();
		await Assert.That(groups.Find("Public") is NotFound).IsTrue().Because("a channel's key names no group");
	}

	[Test]
	public async Task AChangeElsewhere_IsReadIn()
	{
		var (store, groups) = await CreateAsync();
		var changed = new TaskCompletionSource();
		groups.Changed += () => changed.TrySetResult();

		store.ChangeElsewhere(CommGroup.ToJson([new CommGroup("Roleplay", ["Public", "Scenes"])]));
		await changed.Task;

		await Assert.That(groups.Groups.Single().Name).IsEqualTo("Roleplay");
	}
}
