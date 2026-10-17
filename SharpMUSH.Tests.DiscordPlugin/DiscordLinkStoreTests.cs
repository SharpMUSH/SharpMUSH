using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
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

	private async Task ReopenAsync() { await _db.DisposeAsync(); _db = Create(_path); await _db.Migrate(); }

	private static LightningDatabase Create(string path)
		=> new(NullLogger<LightningDatabase>.Instance, new LightningStoreOptions { Path = path, MapSize = 64L << 20 },
			Substitute.For<IPasswordService>(), relations: null);

	private static DiscordChannelLink Link(ulong channel, string name)
		=> new(channel, 900, channel + 1, name, "Guild", Now, "#1:1");

	[Test]
	public async Task AccountOwnershipLookupRejectsAStaleCharacterCreationIdentity()
	{
		var player = await _db.CreatePlayerAsync("ExactOwner", "pw", new SharpMUSH.Library.Models.DBRef(0), new SharpMUSH.Library.Models.DBRef(0), 0);
		var account = await _db.CreateAccountAsync("owner", null, "hash");
		await _db.LinkCharacterToAccountAsync(account.Id!, player);
		await Assert.That(await _db.GetAccountForCharacterAsync(new(player.Number, player.CreationMilliseconds!.Value - 1))).IsNull();
		await Assert.That((await _db.GetAccountForCharacterAsync(player))?.Id).IsEqualTo(account.Id);
		await Assert.That((await _db.GetAccountForCharacterAsync(new(player.Number)))?.Id).IsEqualTo(account.Id);
	}

	[Test]
	public async Task ExternalSpeakerMetadataAndNoCharacterSurviveWorldRestart()
	{
		var identity = new SharpMUSH.Library.Models.ExternalFeedIdentity("discord", "1234", "Original Name");
		var message = new SharpMUSH.Library.Models.SharpFeedMessage(777, "discord", "88", Now, new(-1), identity.DisplayName,
			new(1), "Bridge", null, "", "say", MarkupString.MarkupText.Plain("hello"), identity.DisplayName, ExternalIdentity: identity);
		await _db.SetFeedKindAsync(new("discord", new(1), "Discord", SharpMUSH.Library.Models.FeedSettings.None, new Dictionary<string, string>()));
		await _db.AppendFeedMessageAsync(message, SharpMUSH.Library.Models.FeedSettings.Defaults);
		await ReopenAsync();
		var stored = (await _db.GetFeedMessageAsync(777)).Expect<SharpMUSH.Library.Models.SharpFeedMessage>();
		await Assert.That(stored.ExternalIdentity).IsEqualTo(identity);
		await Assert.That(stored.Speaker.Number).IsEqualTo(-1);
		await Assert.That(stored.SpeakerName).IsEqualTo("[Discord] Original Name (1234)");
	}

	[Test]
	[Arguments(false, true)]
	[Arguments(false, false)]
	[Arguments(true, true)]
	public async Task ChoosingOwnCharacterRechecksOwnershipAfterSendPermission(bool transfer, bool maySend)
	{
		var player = (await _db.GetObjectNodeAsync(await _db.CreatePlayerAsync("OwnSpeaker", "pw", new SharpMUSH.Library.Models.DBRef(0), new SharpMUSH.Library.Models.DBRef(0), 0))).Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>().Expect<SharpMUSH.Library.Models.SharpPlayer>();
		var account = new SharpMUSH.Library.Models.SharpAccount { Id = "mine", Username = "owner", PasswordHash = "" };
		var accountService = Substitute.For<IAccountService>();
		accountService.GetByIdAsync("mine", Arg.Any<CancellationToken>()).Returns(account);
		accountService.GetCharactersAsync("mine", Arg.Any<CancellationToken>()).Returns(new[] { player });
		var owner = account;
		accountService.GetAccountForCharacterAsync(player.Object.DBRef, Arg.Any<CancellationToken>()).Returns(_ => owner);
		var links = Substitute.For<IDiscordLinkStore>(); links.Get(88).Returns(Link(88, "help"));
		var users = Substitute.For<IDiscordAccountStore>();
		var user = new DiscordAccountLink(12, "mine", "member", Now); users.Get(12).Returns(user); users.ForAccount("mine").Returns(user);
		var speakers = new MemorySpeakerStore();
		var accounts = new DiscordAccounts(users, speakers, new DiscordLinkTickets(TimeProvider.System), accountService, Substitute.For<IDiscordLinkedRole>(), TimeProvider.System, NullLogger<DiscordAccounts>.Instance);
		var identities = new DiscordIdentities(links, accounts, speakers, accountService);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = identities.SpeakAsAsync(12, 88, player.Object.Name, _ => { entered.SetResult(); return release.Task; }, default);
		await entered.Task;
		if (transfer) owner = new SharpMUSH.Library.Models.SharpAccount { Id = "other", Username = "other", PasswordHash = "" };
		release.SetResult(maySend);
		await Assert.That(await pending is string).IsEqualTo(!transfer && maySend);
		await Assert.That(speakers.Character(12, 88) is not null).IsEqualTo(!transfer && maySend);
	}

	[Test]
	[Arguments("restricted")]
	[Arguments("unlinked")]
	[Arguments("banned")]
	[Arguments("success")]
	[Arguments("modebusy")]
	[Arguments("cancel-before-run")]
	[Arguments("cancel-after-run")]
	[Arguments("character-gone")]
	[Arguments("character-lock")]
	[Arguments("character-success")]
	public async Task ActualFeedInputRechecksModeAndStandingWhenQueuedSpeechRuns(string change)
	{
		var owner = (await _db.GetObjectNodeAsync(await _db.CreatePlayerAsync("DiscordBridge", "pw", new SharpMUSH.Library.Models.DBRef(0), new SharpMUSH.Library.Models.DBRef(0), 0))).Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>();
		var character = (await _db.GetObjectNodeAsync(await _db.CreatePlayerAsync("QueuedCharacter", "pw", new SharpMUSH.Library.Models.DBRef(0), new SharpMUSH.Library.Models.DBRef(0), 0))).Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>();
		var characterPath = change.StartsWith("character", StringComparison.Ordinal);
		var characterGone = false;
		var mediator = Substitute.For<Mediator.IMediator>();
		mediator.Send(Arg.Any<SharpMUSH.Library.Queries.Database.GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			call.Arg<SharpMUSH.Library.Queries.Database.GetObjectNodeQuery>().DBRef == character.Object().DBRef
				? characterGone ? new SharpMUSH.Library.DiscriminatedUnions.AnyOptionalSharpObject(new SharpMUSH.Library.DiscriminatedUnions.None()) : new SharpMUSH.Library.DiscriminatedUnions.AnyOptionalSharpObject(character)
				: new SharpMUSH.Library.DiscriminatedUnions.AnyOptionalSharpObject(owner));
		var kind = new SharpMUSH.Library.Models.SharpFeedKind("discord", owner.Object().DBRef, "", SharpMUSH.Library.Models.FeedSettings.None, new Dictionary<string, string>());
		var feeds = Substitute.For<IFeedService>();
		feeds.GetKindAsync("discord").Returns(_ => new SharpMUSH.Library.DiscriminatedUnions.Result<SharpMUSH.Library.Models.SharpFeedKind>(kind));
		feeds.SendAsync(Arg.Any<SharpMUSH.Library.ParserInterfaces.IMUSHCodeParser>(), Arg.Any<FeedSend>()).Returns(new SharpMUSH.Library.DiscriminatedUnions.Result<FeedDelivery>(new FeedDelivery(44, [], true)));
		feeds.PassesAsync(Arg.Any<SharpMUSH.Library.Models.SharpFeedKind>(), Arg.Any<SharpMUSH.Library.Models.SharpFeed>(), "send", Arg.Any<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>()).Returns(call => ValueTask.FromResult(call.Arg<SharpMUSH.Library.Models.SharpFeedKind>().Lock("send").Length == 0));
		var publicationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finishPublication = new TaskCompletionSource<SharpMUSH.Library.DiscriminatedUnions.Result<FeedDelivery>>(TaskCreationOptions.RunContinuationsAsynchronously);
		feeds.SendExternalAsync(Arg.Any<SharpMUSH.Library.ParserInterfaces.IMUSHCodeParser>(), Arg.Any<ExternalFeedSend>()).Returns(_ =>
		{
			publicationEntered.TrySetResult();
			return change == "cancel-after-run" ? new ValueTask<SharpMUSH.Library.DiscriminatedUnions.Result<FeedDelivery>>(finishPublication.Task)
				: ValueTask.FromResult<SharpMUSH.Library.DiscriminatedUnions.Result<FeedDelivery>>(new FeedDelivery(44, [], true));
		});
		var links = Substitute.For<IDiscordLinkStore>();
		DiscordChannelLink? current = Link(88, "help") with { Unrestricted = true };
		links.Get(88).Returns(_ => current);
		var accounts = Substitute.For<IDiscordAccountStore>();
		accounts.Get(12).Returns(new DiscordAccountLink(12, "account", "member", Now));
		var standing = Substitute.For<IDiscordStandings>();
		standing.StandingAsync("account", Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(change == "banned" ? DiscordStanding.Banned : DiscordStanding.Active));
		var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Func<ValueTask<SharpMUSH.Library.ParserInterfaces.CallState?>>? queued = null;
		var scheduler = Substitute.For<ITaskScheduler>();
		scheduler.AdmitWork(Arg.Any<Func<ValueTask<SharpMUSH.Library.ParserInterfaces.CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SharpMUSH.Library.Models.DBRef>(), Arg.Any<SharpMUSH.Library.Models.DBRef>(), false).Returns(call =>
		{ queued = call.Arg<Func<ValueTask<SharpMUSH.Library.ParserInterfaces.CallState?>>>(); admitted.TrySetResult(); return ValueTask.FromResult(new SharpMUSH.Library.Models.SchedulerModels.QueueAdmissionResult(1, default)); });
		var parser = Substitute.For<SharpMUSH.Library.ParserInterfaces.IMUSHCodeParser>();
		parser.FromState(Arg.Any<SharpMUSH.Library.ParserInterfaces.ParserState>()).Returns(parser);
		var accountService = Substitute.For<IAccountService>();
		accountService.GetAccountForCharacterAsync(character.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new SharpMUSH.Library.Models.SharpAccount { Id = "account", Username = "member", PasswordHash = "" });
		var speakers = Substitute.For<IDiscordSpeakerStore>(); speakers.Character(12, 88).Returns(character.Object().DBRef.ToString());
		var lifecycle = new DiscordLinkMutationGate();
		using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(lifecycle).AddSingleton(accountService).AddSingleton(speakers).AddSingleton(mediator).AddSingleton(feeds).AddSingleton(links).AddSingleton(accounts).AddSingleton(standing).AddSingleton(scheduler).AddSingleton(parser).BuildServiceProvider();
		var input = new DiscordFeedInput(services, NullLogger<DiscordFeedInput>.Instance);
		var releaseAccess = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var updating = change == "modebusy" ? lifecycle.RunAsync(async () => { await releaseAccess.Task; current = current! with { Unrestricted = false }; return true; }, default) : Task.FromResult(true);
		using var cancellation = new CancellationTokenSource();
		var pending = input.SpeakAsync(new(current!, "account", characterPath ? character.Object().DBRef.ToString() : "", "say", MarkupString.MarkupText.Plain("hi"), "discord/22", characterPath ? null : new("discord", "12", "member"), 12), cancellation.Token);
		if (change == "modebusy")
		{
			await Assert.That(queued is null).IsTrue().Because("the Discord REST wait must happen before admitting engine work");
			releaseAccess.SetResult(); await updating;
		}
		await admitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
		if (change == "cancel-before-run")
		{
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
			await queued!();
			await feeds.DidNotReceive().SendExternalAsync(Arg.Any<SharpMUSH.Library.ParserInterfaces.IMUSHCodeParser>(), Arg.Any<ExternalFeedSend>());
			return;
		}
		if (change == "cancel-after-run")
		{
			var execution = queued!().AsTask(); await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			cancellation.Cancel();
			var restriction = lifecycle.RunAsync(() => Task.FromResult(true), default);
			await Assert.That(restriction.IsCompleted).IsFalse();
			finishPublication.SetResult(new FeedDelivery(44, [], true)); await execution;
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
			await Assert.That(await restriction).IsTrue();
			return;
		}
		if (change == "character-gone") characterGone = true;
		if (change == "character-lock") kind = kind with { Locks = new Dictionary<string, string> { ["send"] = "#FALSE" } };
		if (change == "restricted") current = current! with { Unrestricted = false };
		if (change == "unlinked") current = null;
		await queued!();
		await Assert.That(await pending is Spoken).IsEqualTo(change is "success" or "character-success");
	}

	[Test]
	public async Task AccessModeAndPermissionBaselineSurviveWorldRestart()
	{
		var store = new LightningDiscordLinkStore(_db);
		var original = Link(88, "newbies") with
		{
			Unrestricted = true,
			PermissionHistoryKnown = true,
			GateSnapshot = new DiscordGateSnapshot(new(900, Discord.PermValue.Allow, Discord.PermValue.Deny), new(901, Discord.PermValue.Inherit, Discord.PermValue.Allow))
		};
		await store.SaveAsync(original);
		await ReopenAsync();
		await Assert.That(new LightningDiscordLinkStore(_db).Get(88)).IsEqualTo(original);
	}

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
