using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Discord;
using SharpMUSH.Plugins.Discord.Events;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>
/// <c>@discord</c> and its switches, run as the game runs them, with the plugin registered as the server registers it
/// and no Discord to talk to.
/// </summary>
public class DiscordCommandModuleTests
{
	private const ulong Channel = 4242;
	private const ulong Guild = 900;

	private DiscordPluginHarness _harness = null!;

	[Before(Test)]
	public async Task Start() => _harness = await DiscordPluginHarness.StartAsync();

	[After(Test)]
	public async Task Stop() => await _harness.DisposeAsync();

	private AnySharpObject God => _harness.God;

	private Task LinkAsync(ulong channel, string name, bool unrestricted = false, DiscordLinkFault? fault = null)
		=> _harness.Get<IDiscordLinkStore>().SaveAsync(
			new DiscordChannelLink(channel, Guild, channel + 1, name, "Guild", DiscordPluginHarness.Now, "#1:1001", fault, unrestricted)).AsTask();

	[Test]
	public async Task WithoutDiscordAdmin_EverySwitchButSpeakIsRefused()
	{
		var mortal = _harness.Mortal(7, "Mortal");

		var result = await _harness.RunAsync(mortal, "link", Channel.ToString());

		await Assert.That(result).IsEqualTo("#-1 PERMISSION DENIED");
		await Assert.That(_harness.ToldTo(mortal)).IsEquivalentTo(["[DISCORD] Error: Permission denied. You need the discord.admin permission."]);
		await Assert.That(_harness.Get<IDiscordLinkStore>().List()).IsEmpty();
	}

	[Test]
	public async Task Overview_ShowsTheConnectionAndThatNothingIsLinked()
	{
		var result = await _harness.RunAsync(God, "");

		await Assert.That(result).IsEqualTo("");
		var panel = _harness.ToldTo(God).Single();
		await Assert.That(panel).Contains("Discord");
		await Assert.That(panel).Contains("connecting");
		await Assert.That(panel).Contains("not mirrored to Discord");
		await Assert.That(panel).Contains("No channels are linked. @discord/link <channel id> links one.");
	}

	[Test]
	public async Task Overview_ListsEachLinkWithItsState()
	{
		await LinkAsync(Channel, "general");
		await LinkAsync(Channel + 10, "ooc", unrestricted: true);
		await LinkAsync(Channel + 20, "broken", fault: new DiscordLinkFault(50013, "Missing Permissions", DiscordPluginHarness.Now));

		await _harness.RunAsync(God, "");

		var panel = _harness.ToldTo(God).Single();
		await Assert.That(panel).Contains("#general");
		await Assert.That(panel).Contains("#ooc");
		await Assert.That(panel).Contains("unrestricted (Discord identities allowed)");
		await Assert.That(panel).Contains("stopped: Missing Permissions");
		await Assert.That(panel).Contains($"@discord/resume {Channel + 20}");
	}

	[Test]
	[Arguments("link")]
	[Arguments("unlink")]
	[Arguments("resume")]
	[Arguments("gate")]
	public async Task AChannelSwitch_WithoutAChannelId_SaysHowToFindOne(string action)
	{
		var result = await _harness.RunAsync(God, action, "general");

		await Assert.That(result).IsEqualTo("#-1 INVALID CHANNEL ID");
		await Assert.That(_harness.ToldTo(God).Single())
			.IsEqualTo($"[DISCORD] Warning: @discord/{action} takes a channel's id. In Discord, turn on Developer Mode, then right-click the channel and Copy Channel ID.");
	}

	[Test]
	public async Task Link_WhileTheBotIsAway_ReturnsTheIdAndTellsTheAdminLater()
	{
		var result = await _harness.RunAsync(God, "link", Channel.ToString());

		await Assert.That(result).IsEqualTo(Channel.ToString());
		var answer = await _harness.WaitForAsync(God, "[DISCORD]");
		await Assert.That(answer).IsEqualTo("[DISCORD] Error: The bot is not connected to Discord. @discord shows why.");
		await Assert.That(_harness.Get<IDiscordLinkStore>().List()).IsEmpty();
	}

	[Test]
	public async Task Link_OfALinkedChannel_SaysSo()
	{
		await LinkAsync(Channel, "general");

		await _harness.RunAsync(God, "link", Channel.ToString());

		await Assert.That(await _harness.WaitForAsync(God, "[DISCORD]")).IsEqualTo("[DISCORD] Error: #general is already linked.");
	}

	[Test]
	[Arguments("unlink")]
	[Arguments("resume")]
	[Arguments("gate")]
	[Arguments("unrestricted")]
	public async Task AChannelSwitch_OnAnUnlinkedChannel_SaysItIsNotLinked(string action)
	{
		var result = await _harness.RunAsync(God, action, Channel.ToString());

		await Assert.That(result).IsEqualTo(Channel.ToString());
		await Assert.That(await _harness.WaitForAsync(God, "[DISCORD]")).IsEqualTo("[DISCORD] Warning: That channel is not linked.");
	}

	[Test]
	public async Task Resume_StartsAStoppedLinkAgain()
	{
		await LinkAsync(Channel, "general", fault: new DiscordLinkFault(50013, "Missing Permissions", DiscordPluginHarness.Now));

		await _harness.RunAsync(God, "resume", Channel.ToString());

		await Assert.That(await _harness.WaitForAsync(God, "[DISCORD]")).IsEqualTo("[DISCORD] Done: #general posts again.");
		await Assert.That(_harness.Get<IDiscordLinkStore>().Get(Channel)!.Fault).IsNull();
	}

	[Test]
	public async Task Unlink_WhileTheBotIsAway_KeepsTheLinkAndSaysWhy()
	{
		await LinkAsync(Channel, "general");

		await _harness.RunAsync(God, "unlink", Channel.ToString());

		var answer = await _harness.WaitForAsync(God, "[DISCORD]");
		await Assert.That(answer).IsEqualTo(
			"[DISCORD] Error: Discord permission update failed: The bot is not connected to Discord. Retry the operation after fixing the bot's permissions.");
		await Assert.That(_harness.Get<IDiscordLinkStore>().Get(Channel)).IsNotNull();
	}

	[Test]
	public async Task Connect_AsksTheGatewayToLogInAgain()
	{
		var result = await _harness.RunAsync(God, "connect");

		await Assert.That(result).IsEqualTo("");
		await Assert.That(_harness.ToldTo(God).Single()).IsEqualTo("[DISCORD] Done: Connecting to Discord. @discord shows how it went.");
		await Assert.That(_harness.Get<DiscordGatewayService>().State.Status).IsEqualTo(DiscordGatewayStatus.Connecting);
	}

	[Test]
	public async Task Bans_TurnMirroringOnAndOff()
	{
		var on = await _harness.RunAsync(God, "bans", "on");
		var mirroredAfterOn = _harness.Get<IDiscordSettingsStore>().Get().MirrorBans;
		var off = await _harness.RunAsync(God, "bans", "OFF");

		await Assert.That(on).IsEqualTo("1");
		await Assert.That(mirroredAfterOn).IsTrue();
		await Assert.That(off).IsEqualTo("0");
		await Assert.That(_harness.Get<IDiscordSettingsStore>().Get().MirrorBans).IsFalse();
		await Assert.That(_harness.ToldTo(God)).IsEquivalentTo([
			"[DISCORD] Done: A game ban now bans the account's Discord member in every server the bot is in. The bot needs Ban Members there.",
			"[DISCORD] Done: Game bans are no longer mirrored. Bans the bot already made are lifted when their game bans end.",
		]);
	}

	[Test]
	public async Task Bans_AloneListsWhoTheBotBanned()
	{
		await _harness.Get<IDiscordBanStore>().SaveAsync(new DiscordMirroredBan(555, "acct", [Guild, Guild + 1], DiscordPluginHarness.Now));

		var result = await _harness.RunAsync(God, "bans");

		await Assert.That(result).IsEqualTo("");
		var panel = _harness.ToldTo(God).Single();
		await Assert.That(panel).Contains("Discord bans");
		await Assert.That(panel).Contains("Game bans are not mirrored to Discord. @discord/bans on mirrors them.");
		await Assert.That(panel).Contains("555");
		await Assert.That(panel).Contains("2026-10-15 03:00");
	}

	[Test]
	public async Task Bans_WithNobodyBanned_SaysSo()
	{
		await _harness.RunAsync(God, "bans");

		await Assert.That(_harness.ToldTo(God).Single()).Contains("The bot has banned nobody.");
	}

	[Test]
	public async Task Bans_WithAnythingButOnOrOff_SaysWhatItTakes()
	{
		var result = await _harness.RunAsync(God, "bans", "maybe");

		await Assert.That(result).IsEqualTo("#-1 ON OR OFF");
		await Assert.That(_harness.ToldTo(God).Single()).IsEqualTo("[DISCORD] Warning: @discord/bans takes on or off.");
	}

	[Test]
	public async Task Speak_WithoutAChannelId_SaysHowToFindOne()
	{
		var mortal = _harness.Mortal(7, "Mortal");

		var result = await _harness.RunAsync(mortal, "speak", "general");

		await Assert.That(result).IsEqualTo("#-1 INVALID CHANNEL ID");
		await Assert.That(_harness.ToldTo(mortal).Single()).StartsWith("[DISCORD] Warning: @discord/speak takes a linked channel's id.");
	}

	[Test]
	public async Task Speak_InAnUnlinkedChannel_IsRefusedToAnyone()
	{
		var mortal = _harness.Mortal(7, "Mortal");

		var result = await _harness.RunAsync(mortal, "speak", Channel.ToString());

		await Assert.That(result).IsEqualTo("#-1");
		await Assert.That(_harness.ToldTo(mortal).Single()).IsEqualTo("[DISCORD] Error: That channel is not linked.");
	}

	[Test]
	public async Task Speak_ByACharacterWithNoAccount_SaysItCannotSpeak()
	{
		await LinkAsync(Channel, "general");
		var mortal = _harness.Mortal(7, "Mortal");

		await _harness.RunAsync(mortal, "speak", Channel.ToString());

		await Assert.That(_harness.ToldTo(mortal).Single()).IsEqualTo("[DISCORD] Error: Mortal is not on a game account, so it cannot speak in Discord.");
	}

	[Test]
	public async Task SpeakOff_ByACharacterWithNoAccount_IsRefused()
	{
		await LinkAsync(Channel, "general");
		var mortal = _harness.Mortal(7, "Mortal");

		var result = await _harness.RunAsync(mortal, "speak/off", Channel.ToString());

		await Assert.That(result).IsEqualTo("#-1");
		await Assert.That(_harness.ToldTo(mortal).Single()).IsEqualTo("[DISCORD] Error: Mortal is not on a game account, so it cannot speak in Discord.");
	}

	[Test]
	public async Task Map_AloneListsTheMappings()
	{
		await _harness.Get<IDiscordRoleStore>().SaveAsync(
			new DiscordRoleMapping(77, Guild, "Builders", "builder", DiscordRoleMode.Enforce, DiscordPluginHarness.Now, "#1:1001"));

		var result = await _harness.RunAsync(God, "map");

		await Assert.That(result).IsEqualTo("");
		var panel = _harness.ToldTo(God).Single();
		await Assert.That(panel).Contains("Discord roles");
		await Assert.That(panel).Contains("@Builders");
		await Assert.That(panel).Contains("builder");
		await Assert.That(panel).Contains("put back");
		await Assert.That(panel).Contains("broken: the bot is not connected");
	}

	[Test]
	public async Task Map_WithNoMappings_SaysHowToMapOne()
	{
		await _harness.RunAsync(God, "map");

		await Assert.That(_harness.ToldTo(God).Single()).Contains("No roles are mapped. @discord/map <game role>=<Discord role> maps one.");
	}

	[Test]
	public async Task Map_WithoutADiscordRole_SaysWhatItTakes()
	{
		var result = await _harness.RunAsync(God, "map", "builder");

		await Assert.That(result).IsEqualTo("#-1");
		await Assert.That(_harness.ToldTo(God).Single())
			.IsEqualTo("[DISCORD] Warning: @discord/map <game role>=<Discord role> maps one; @discord/map alone lists them.");
	}

	[Test]
	public async Task Map_OfAnUnknownGameRole_SaysSo()
	{
		_harness.Capabilities.GetObjectGrantsAsync(God, Arg.Any<CancellationToken>()).Returns(ObjectGrants.None);
		_harness.Roles.GetRoleAsync("nosuchrole", Arg.Any<CancellationToken>()).Returns(new NotFound());

		var result = await _harness.RunAsync(God, "map", "nosuchrole", "Builders");

		await Assert.That(result).IsEqualTo("#-1");
		await Assert.That(_harness.ToldTo(God).Single()).IsEqualTo("[DISCORD] Error: No game role is called nosuchrole. @role lists them.");
	}

	[Test]
	public async Task Unmap_RemovesAMappingByItsRoleName()
	{
		await _harness.Get<IDiscordRoleStore>().SaveAsync(
			new DiscordRoleMapping(77, Guild, "Builders", "builder", DiscordRoleMode.Report, DiscordPluginHarness.Now, "#1:1001"));

		var result = await _harness.RunAsync(God, "unmap", "@builders");

		await Assert.That(result).IsEqualTo("77");
		await Assert.That(_harness.ToldTo(God).Single()).IsEqualTo("[DISCORD] Done: Builders is no longer mapped to builder. Members keep it as it is.");
		await Assert.That(_harness.Get<IDiscordRoleStore>().Mappings()).IsEmpty();
	}

	[Test]
	public async Task Unmap_OfAnUnmappedRole_SaysSo()
	{
		var result = await _harness.RunAsync(God, "unmap", "Builders");

		await Assert.That(result).IsEqualTo("#-1");
		await Assert.That(_harness.ToldTo(God).Single()).IsEqualTo("[DISCORD] Warning: Builders is not a mapped Discord role. @discord/map lists them.");
	}

	[Test]
	public async Task EnforceAndReport_SwitchAMappingsMode()
	{
		var store = _harness.Get<IDiscordRoleStore>();
		await store.SaveAsync(new DiscordRoleMapping(77, Guild, "Builders", "builder", DiscordRoleMode.Report, DiscordPluginHarness.Now, "#1:1001"));

		var enforced = await _harness.RunAsync(God, "map/enforce", "77");
		var modeAfterEnforce = store.Mapping(77)!.Mode;
		var reported = await _harness.RunAsync(God, "map/report", "77");

		await Assert.That(enforced).IsEqualTo("77");
		await Assert.That(modeAfterEnforce).IsEqualTo(DiscordRoleMode.Enforce);
		await Assert.That(reported).IsEqualTo("77");
		await Assert.That(store.Mapping(77)!.Mode).IsEqualTo(DiscordRoleMode.Report);
		await Assert.That(_harness.ToldTo(God)).IsEquivalentTo([
			"[DISCORD] Done: A change to Builders made by hand on Discord is put back at once, and still listed by @discord/drift.",
			"[DISCORD] Done: A change to Builders made by hand on Discord is listed by @discord/drift and left for you to decide.",
		]);
	}

	[Test]
	public async Task Drift_ListsRolesChangedByHand()
	{
		var store = _harness.Get<IDiscordRoleStore>();
		await store.SaveAsync(new DiscordRoleMapping(77, Guild, "Builders", "builder", DiscordRoleMode.Report, DiscordPluginHarness.Now, "#1:1001"));
		await store.RecordAsync(77, new DiscordDrift(555, "Alice", true, DiscordPluginHarness.Now));

		var result = await _harness.RunAsync(God, "drift");

		await Assert.That(result).IsEqualTo("");
		var panel = _harness.ToldTo(God).Single();
		await Assert.That(panel).Contains("Discord roles changed by hand");
		await Assert.That(panel).Contains("Alice");
		await Assert.That(panel).Contains("given it, without builder in the");
	}

	[Test]
	public async Task Drift_WithNone_SaysSo()
	{
		await _harness.RunAsync(God, "drift");

		await Assert.That(_harness.ToldTo(God).Single()).Contains("No mapped role was changed by hand.");
	}

	[Test]
	public async Task DriftApply_PutsRolesBack()
	{
		await _harness.Get<IDiscordRoleStore>().SaveAsync(
			new DiscordRoleMapping(77, Guild, "Builders", "builder", DiscordRoleMode.Report, DiscordPluginHarness.Now, "#1:1001"));

		var all = await _harness.RunAsync(God, "drift/apply");
		var one = await _harness.RunAsync(God, "drift/apply", "Builders");
		var unknown = await _harness.RunAsync(God, "drift/apply", "Nobody");

		await Assert.That(all).IsEqualTo("");
		await Assert.That(one).IsEqualTo("77");
		await Assert.That(unknown).IsEqualTo("#-1");
		await Assert.That(_harness.ToldTo(God)).IsEquivalentTo([
			"[DISCORD] Done: Putting every role changed by hand back to the game's roles.",
			"[DISCORD] Done: Putting Builders back to the game's roles wherever it was changed by hand.",
			"[DISCORD] Warning: Nobody is not a mapped Discord role. @discord/map lists them.",
		]);
	}

	[Test]
	public async Task Event_AloneListsWhatIsPublished()
	{
		await _harness.Get<IDiscordEventStore>().SaveAsync(
			new DiscordEventRecord("ball", Guild, 31, "The Winter Ball", DiscordPluginHarness.Now.AddDays(2), DiscordPluginHarness.Now));

		var result = await _harness.RunAsync(God, "event");

		await Assert.That(result).IsEqualTo("");
		var panel = _harness.ToldTo(God).Single();
		await Assert.That(panel).Contains("Discord events");
		await Assert.That(panel).Contains("ball");
		await Assert.That(panel).Contains("The Winter Ball");
		await Assert.That(panel).Contains("2026-10-17 03:00");
	}

	[Test]
	public async Task Event_WithNothingPublished_SaysHowToPublish()
	{
		await _harness.RunAsync(God, "event");

		await Assert.That(_harness.ToldTo(God).Single()).Contains("Nothing is published. @discord/event <key>=<json> publishes an event.");
	}

	[Test]
	[Arguments("event", "", "{}")]
	[Arguments("event/start", "", null)]
	[Arguments("event/end", "", null)]
	public async Task Event_WithoutAKey_SaysItNeedsOne(string switches, string left, string? right)
	{
		var result = await _harness.RunAsync(God, switches, left, right);

		await Assert.That(result).IsEqualTo("#-1 NO KEY");
		await Assert.That(_harness.ToldTo(God).Single()).IsEqualTo("[DISCORD] Warning: @discord/event needs a key: @discord/event <key>=<json>.");
	}

	[Test]
	public async Task Event_WithAKeyButNoEvent_SaysWhatItTakes()
	{
		var result = await _harness.RunAsync(God, "event", "ball");

		await Assert.That(result).IsEqualTo("#-1 NO EVENT");
		await Assert.That(_harness.ToldTo(God).Single())
			.IsEqualTo("[DISCORD] Warning: @discord/event <key>=<json> publishes an event; /start and /end change it.");
	}

	[Test]
	public async Task Event_WithJsonDiscordRefuses_SaysWhy()
	{
		var result = await _harness.RunAsync(God, "event", "ball", "not json");

		await Assert.That(result).IsEqualTo("#-1 BAD EVENT");
		await Assert.That(_harness.ToldTo(God).Single()).StartsWith("[DISCORD] Warning: The event must be a JSON object");
	}

	[Test]
	public async Task Event_Publish_WhileTheBotIsAway_ReturnsTheKeyAndSaysLater()
	{
		var result = await _harness.RunAsync(God, "event", "ball",
			"""{"name":"The Winter Ball","start":1792206000,"location":"The ballroom"}""");

		await Assert.That(result).IsEqualTo("ball");
		await Assert.That(await _harness.WaitForAsync(God, "[DISCORD]")).IsEqualTo("[DISCORD] Error: The bot is not connected to Discord.");
	}

	[Test]
	[Arguments("event/start")]
	[Arguments("event/end")]
	public async Task Event_StartAndEnd_WhileTheBotIsAway_SayItIsNotConnected(string switches)
	{
		var result = await _harness.RunAsync(God, switches, "ball");

		await Assert.That(result).IsEqualTo("ball");
		await Assert.That(await _harness.WaitForAsync(God, "[DISCORD]")).IsEqualTo("[DISCORD] Error: The bot is not connected to Discord.");
	}

	private async Task<AnySharpObject> AccountHolderAsync(bool discordLinked)
	{
		var mortal = _harness.Mortal(7, "Mortal");
		_harness.Accounts.GetAccountForCharacterAsync(mortal.Object().DBRef, Arg.Any<CancellationToken>())
			.Returns(new SharpAccount { Id = "acct", Username = "alice" });
		if (discordLinked)
		{
			await _harness.Get<IDiscordAccountStore>().SaveAsync(new DiscordAccountLink(555, "acct", "alice", DiscordPluginHarness.Now));
		}

		return mortal;
	}

	private void FeedKindAllowsSending(bool allowed)
	{
		var kind = new SharpFeedKind(DiscordChannelLink.FeedKind, new DBRef(1), "Discord", FeedSettings.Defaults,
			new Dictionary<string, string>());
		_harness.Feeds.GetKindAsync(DiscordChannelLink.FeedKind).Returns(new Result<SharpFeedKind>(kind));
		_harness.Feeds.CanRunAsync(Arg.Any<AnySharpObject>(), kind).Returns(allowed);
	}

	[Test]
	public async Task Speak_FromAnAccountNotLinkedToDiscord_SaysHowToLinkIt()
	{
		await LinkAsync(Channel, "general");
		var mortal = await AccountHolderAsync(discordLinked: false);

		await _harness.RunAsync(mortal, "speak", Channel.ToString());

		await Assert.That(_harness.ToldTo(mortal).Single())
			.IsEqualTo("[DISCORD] Error: Your game account is not linked to Discord. Type /link in Discord to link it.");
	}

	[Test]
	public async Task Speak_WithoutLeaveToSendOnTheFeed_IsRefused()
	{
		await LinkAsync(Channel, "general");
		var mortal = await AccountHolderAsync(discordLinked: true);
		FeedKindAllowsSending(false);

		var result = await _harness.RunAsync(mortal, "speak", Channel.ToString());

		await Assert.That(result).IsEqualTo("#-1");
		await Assert.That(_harness.ToldTo(mortal).Single())
			.IsEqualTo($"[DISCORD] Error: Mortal may not send to discord/{Channel}, so it cannot speak there.");
		await Assert.That(_harness.Get<IDiscordSpeakerStore>().Character(555, Channel)).IsNull();
	}

	[Test]
	public async Task Speak_ThenSpeakOff_ChoosesAndDropsTheCharacterADiscordUserSpeaksAs()
	{
		await LinkAsync(Channel, "general");
		var mortal = await AccountHolderAsync(discordLinked: true);
		FeedKindAllowsSending(true);
		var speakers = _harness.Get<IDiscordSpeakerStore>();

		var spoke = await _harness.RunAsync(mortal, "speak", Channel.ToString());
		var speakingAs = speakers.Character(555, Channel);
		var stopped = await _harness.RunAsync(mortal, "speak/off", Channel.ToString());
		var again = await _harness.RunAsync(mortal, "speak/off", Channel.ToString());

		await Assert.That(spoke).IsEqualTo(Channel.ToString());
		await Assert.That(speakingAs).IsEqualTo(mortal.Object().DBRef.ToString());
		await Assert.That(stopped).IsEqualTo(Channel.ToString());
		await Assert.That(again).IsEqualTo("#-1");
		await Assert.That(speakers.Character(555, Channel)).IsNull();
		await Assert.That(_harness.ToldTo(mortal)).IsEquivalentTo([
			"[DISCORD] Done: Your messages as alice in #general now reach the game as Mortal. Anyone who reads both can tell that alice plays Mortal.",
			"[DISCORD] Done: Mortal no longer speaks for alice in #general.",
			"[DISCORD] Error: Mortal does not speak in #general.",
		], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("gate")]
	[Arguments("unrestricted")]
	public async Task GateAndUnrestricted_WhileTheBotIsAway_LeaveTheLinkAsItWas(string action)
	{
		await LinkAsync(Channel, "general");

		await _harness.RunAsync(God, action, Channel.ToString());

		await Assert.That(await _harness.WaitForAsync(God, "[DISCORD]")).IsEqualTo(
			"[DISCORD] Error: Could not change access in #general: Discord permission update failed: The bot is not connected to Discord. Retry the operation after fixing the bot's permissions.");
		await Assert.That(_harness.Get<IDiscordLinkStore>().Get(Channel)!.Unrestricted).IsFalse();
	}
}
