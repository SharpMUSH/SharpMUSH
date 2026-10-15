using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordLinkStoreTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-discord-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Close()
	{
		await _db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer join.
		}
	}

	private static LightningDatabase Create(string path)
		=> new(NullLogger<LightningDatabase>.Instance, new LightningStoreOptions { Path = path, MapSize = 64L << 20 },
			Substitute.For<IPasswordService>(), relations: null);

	private static DiscordChannelLink Link(ulong channel, string name)
		=> new(channel, 900, channel + 1, name, "Guild", Now, "#1:1");

	[Test]
	public async Task ALink_IsKept_AndFoundByItsChannel()
	{
		var store = new LightningDiscordLinkStore(_db);
		var faulted = Link(20, "ooc") with { Fault = new DiscordLinkFault(50013, "Missing a permission.", Now) };

		await store.SaveAsync(Link(10, "general"));
		await store.SaveAsync(faulted);

		await Assert.That(store.Get(20)).IsEqualTo(faulted);
		await Assert.That(store.List().Select(link => link.ChannelId)).IsEquivalentTo(new ulong[] { 10, 20 });
		await Assert.That(store.Get(30)).IsNull();
	}

	[Test]
	public async Task SavingAgain_ReplacesTheChannelsLink()
	{
		var store = new LightningDiscordLinkStore(_db);
		await store.SaveAsync(Link(10, "general"));

		await store.SaveAsync(Link(10, "general") with { WebhookId = 77 });

		await Assert.That(store.List().Single().WebhookId).IsEqualTo(77UL);
	}

	[Test]
	public async Task Removing_ForgetsOnlyThatChannel()
	{
		var store = new LightningDiscordLinkStore(_db);
		await store.SaveAsync(Link(10, "general"));
		await store.SaveAsync(Link(20, "ooc"));

		await Assert.That(await store.RemoveAsync(10)).IsTrue();
		await Assert.That(await store.RemoveAsync(10)).IsFalse();

		await Assert.That(store.List().Select(link => link.ChannelId)).IsEquivalentTo(new ulong[] { 20 });
	}

	[Test]
	public async Task Links_OutliveARestart()
	{
		var path = _path + "-restart";
		await using (var before = Create(path))
		{
			await before.Migrate();
			await new LightningDiscordLinkStore(before).SaveAsync(Link(10, "general"));
		}

		await using var after = Create(path);
		await after.Migrate();

		await Assert.That(new LightningDiscordLinkStore(after).Get(10)?.ChannelName).IsEqualTo("general");
	}

	[Test]
	public async Task AnAccountLink_IsFoundByUserAndByAccount_UntilItIsUnlinked()
	{
		var store = new LightningDiscordAccountStore(_db);
		var link = new DiscordAccountLink(77, "acct-1", "ann", Now);
		await store.SaveAsync(link);
		await store.SaveGuildAsync(new DiscordGuildRecord(900, 55));

		var reopened = new LightningDiscordAccountStore(_db);
		await Assert.That(reopened.Get(77)).IsEqualTo(link);
		await Assert.That(reopened.ForAccount("acct-1")).IsEqualTo(link);
		await Assert.That(reopened.Guild(900)?.LinkedRoleId).IsEqualTo(55UL);

		await reopened.SaveAsync(link with { UnlinkedAt = Now });
		await Assert.That(reopened.ForAccount("acct-1")).IsNull();
		await Assert.That(reopened.Get(77)?.Linked).IsFalse();
	}

	[Test]
	public async Task SpeakingIdentities_ArePerChannel_AndForgottenTogether()
	{
		var store = new LightningDiscordSpeakerStore(_db);
		await store.SetAsync(77, 10, "#5:1");
		await store.SetAsync(77, 20, "#6:1");
		await store.SetAsync(77, 10, "#7:1");
		await store.SetAsync(88, 10, "#8:1");

		var reopened = new LightningDiscordSpeakerStore(_db);
		await Assert.That(reopened.Character(77, 10)).IsEqualTo("#7:1");
		await Assert.That(reopened.Character(77, 20)).IsEqualTo("#6:1");
		await Assert.That(await reopened.ClearAsync(77, 20)).IsTrue();
		await Assert.That(await reopened.ClearAsync(77, 20)).IsFalse();

		await reopened.ForgetAsync(77);
		await Assert.That(reopened.Character(77, 10)).IsNull();
		await Assert.That(reopened.Character(88, 10)).IsEqualTo("#8:1");
	}

	[Test]
	public async Task MessagePairs_AreForgottenByAge()
	{
		var pairs = new LightningDiscordMessagePairs(_db);
		var old = Discord.SnowflakeUtils.ToSnowflake(Now.AddDays(-100));
		var young = Discord.SnowflakeUtils.ToSnowflake(Now.AddDays(-1));
		await pairs.SaveAsync(new DiscordMessagePair(old, 10, [1]));
		await pairs.SaveAsync(new DiscordMessagePair(young, 10, [2, 3]));

		await Assert.That(await pairs.ForgetBeforeAsync(Now.AddDays(-90))).IsEqualTo(1);
		await Assert.That(pairs.Get(old)).IsNull();
		await Assert.That(pairs.Get(young)?.LineIds).IsEquivalentTo(new long[] { 2, 3 });
		await Assert.That(await pairs.RemoveAsync(young)).IsTrue();
	}

	[Test]
	public async Task ARoleMapping_IsKept_AndRemovingItDropsItsDrift()
	{
		var store = new LightningDiscordRoleStore(_db);
		var mapping = new DiscordRoleMapping(500, 900, "Builders", "builder", DiscordRoleMode.Report, Now, "#1:1");
		await store.SaveAsync(mapping);
		await store.RecordAsync(500, new DiscordDrift(7, "ann", true, Now));

		await Assert.That(store.Mapping(500)).IsEqualTo(mapping);
		await Assert.That(store.Drifted(500, 7)).IsTrue();

		await Assert.That(await store.RemoveAsync(500)).IsTrue();
		await Assert.That(store.Mapping(500)).IsNull();
		await Assert.That(store.Drifted(500, 7)).IsFalse();
		await Assert.That(await store.RemoveAsync(500)).IsFalse();
	}

	[Test]
	public async Task Drift_KeepsOnePerMember_UntilSettled()
	{
		var store = new LightningDiscordRoleStore(_db);
		await store.SaveAsync(new DiscordRoleMapping(500, 900, "Builders", "builder", DiscordRoleMode.Report, Now, "#1:1"));
		await store.RecordAsync(500, new DiscordDrift(7, "ann", true, Now));
		await store.RecordAsync(500, new DiscordDrift(7, "ann", false, Now.AddMinutes(1)));
		await store.RecordAsync(500, new DiscordDrift(8, "bo", true, Now));

		var members = store.Drift().Single().Members;
		await Assert.That(members.Select(drift => drift.UserId)).IsEquivalentTo(new ulong[] { 7, 8 });
		await Assert.That(members.Single(drift => drift.UserId == 7).Added).IsFalse();

		await store.SettleAsync(500, 7);
		await store.SettleAsync(500, 8);
		await Assert.That(store.Drift()).IsEmpty();
	}

	/// <summary>A pass that worked out drift before an unmap must not write it back after; a later mapping starts clean.</summary>
	[Test]
	public async Task Drift_OnARoleNoLongerMapped_IsDropped()
	{
		var store = new LightningDiscordRoleStore(_db);
		await store.RecordAsync(600, new DiscordDrift(7, "ann", true, Now));

		await Assert.That(store.Drifted(600, 7)).IsFalse();
		await Assert.That(store.Drift()).IsEmpty();
	}
}
