using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordInboundTests
{
	private const ulong Channel = 4242;
	private const ulong Ann = 1001;
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
	private static readonly DiscordChannelLink Link = new(Channel, 900, 1, "general", "Guild", Now, "#1:1");

	private sealed class FakeGame : IDiscordFeedInput
	{
		public List<DiscordSpeech> Spoken { get; } = [];
		public List<long> Removed { get; } = [];
		public string? Refusal { get; set; }

		public Task<SpeechOutcome> SpeakAsync(DiscordSpeech speech, CancellationToken cancellationToken)
		{
			if (Refusal is { } reason)
			{
				return Task.FromResult<SpeechOutcome>(new Refused(reason));
			}

			Spoken.Add(speech);
			return Task.FromResult<SpeechOutcome>(new Spoken(Spoken.Count));
		}

		public Task<string?> NameAsync(string character, CancellationToken cancellationToken)
			=> Task.FromResult<string?>(character == "#5:1" ? "Annabel" : null);

		public Task<bool> RemoveLineAsync(long lineId, DiscordChannelLink link, ulong messageId, CancellationToken cancellationToken)
		{
			Removed.Add(lineId);
			return Task.FromResult(true);
		}
	}

	private sealed class FakeDiscord : IDiscordMessages
	{
		public List<ulong> Deleted { get; } = [];
		public List<(ulong User, string Text)> Told { get; } = [];

		public Task DeleteAsync(ulong channelId, ulong messageId, string reason, CancellationToken cancellationToken)
		{
			Deleted.Add(messageId);
			return Task.CompletedTask;
		}

		public Task DirectMessageAsync(ulong userId, string text, CancellationToken cancellationToken)
		{
			Told.Add((userId, text));
			return Task.CompletedTask;
		}
	}

	private sealed class Bridge
	{
		public IDiscordLinkStore Links { get; } = Substitute.For<IDiscordLinkStore>();
		public IDiscordAccountStore Accounts { get; } = Substitute.For<IDiscordAccountStore>();
		public MemorySpeakerStore Speakers { get; } = new();
		public MemoryMessagePairs Pairs { get; } = new();
		public FakeGame Game { get; } = new();
		public FakeDiscord Discord { get; } = new();
		public DiscordInbound Inbound { get; }

		public Bridge()
		{
			Links.Get(Channel).Returns(Link);
			Inbound = new DiscordInbound(Links, Accounts, Speakers, Pairs, Game, Discord, NullLogger<DiscordInbound>.Instance);
		}

		public void Linked(ulong user)
			=> Accounts.Get(user).Returns(new DiscordAccountLink(user, "acct-1", "ann", Now));
	}

	private static DiscordIncoming Message(string content, ulong id = 777, ulong author = Ann, bool automated = false,
		ulong channel = Channel, DiscordAttachment[]? attachments = null, string? replyingTo = null, ulong? replyingToId = null,
		Dictionary<ulong, string>? users = null)
		=> new(id, channel, author, "ann", automated, content, attachments ?? [], replyingTo, replyingToId,
			users ?? new Dictionary<ulong, string>(), new Dictionary<ulong, string>(), new Dictionary<ulong, string>());

	[Test]
	public async Task ALinkedMember_SpeaksAsTheirIdentity_WithTheMessageAsOrigin()
	{
		var bridge = new Bridge();
		bridge.Linked(Ann);
		await bridge.Speakers.SetAsync(Ann, Channel, "#5:1");

		await bridge.Inbound.ReceivedAsync(Message("Hello, game."), CancellationToken.None);

		var speech = bridge.Game.Spoken.Single();
		await Assert.That(speech.Character).IsEqualTo("#5:1");
		await Assert.That(speech.AccountId).IsEqualTo("acct-1");
		await Assert.That(speech.Style).IsEqualTo(FeedStyles.Say);
		await Assert.That(speech.Text.ToPlainText()).IsEqualTo("Hello, game.");
		await Assert.That(speech.Origin).IsEqualTo("discord/777");
		await Assert.That(bridge.Discord.Deleted).IsEmpty().Because("the original goes only once the game's version is posted");
	}

	[Test]
	[Arguments(":waves.", "pose", "waves.")]
	[Arguments(";'s here.", "semipose", "'s here.")]
	[Arguments(":", "say", ":")]
	public async Task ALeadingColon_Poses(string content, string style, string text)
	{
		var bridge = new Bridge();
		bridge.Linked(Ann);
		await bridge.Speakers.SetAsync(Ann, Channel, "#5:1");

		await bridge.Inbound.ReceivedAsync(Message(content), CancellationToken.None);

		var speech = bridge.Game.Spoken.Single();
		await Assert.That(speech.Style).IsEqualTo(style);
		await Assert.That(speech.Text.ToPlainText()).IsEqualTo(text);
	}

	[Test]
	public async Task MentionsAndReplies_ReadAsSpeakingIdentities_OrDiscordNames()
	{
		var bridge = new Bridge();
		bridge.Linked(Ann);
		await bridge.Speakers.SetAsync(Ann, Channel, "#5:1");
		var users = new Dictionary<ulong, string> { [Ann] = "ann", [2002] = "Bob" };

		await bridge.Inbound.ReceivedAsync(Message("hi <@1001> and <@2002>", replyingTo: "ann", replyingToId: Ann, users: users,
			attachments: [new DiscordAttachment("map.png", "https://cdn.example/map.png")]), CancellationToken.None);

		await Assert.That(bridge.Game.Spoken.Single().Text.ToPlainText())
			.IsEqualTo("(replying to Annabel) hi @Annabel and @Bob map.png");
	}

	[Test]
	public async Task AnUnlinkedMember_IsRemoved_AndToldOnce()
	{
		var bridge = new Bridge();

		await bridge.Inbound.ReceivedAsync(Message("first", id: 1), CancellationToken.None);
		await bridge.Inbound.ReceivedAsync(Message("second", id: 2), CancellationToken.None);

		await Assert.That(bridge.Game.Spoken).IsEmpty();
		await Assert.That(bridge.Discord.Deleted).IsEquivalentTo(new ulong[] { 1, 2 });
		await Assert.That(bridge.Discord.Told.Single().Text).Contains("Type /link");
	}

	[Test]
	public async Task ALinkedMemberWithNoIdentity_IsToldOnce_AndTheirMessageStays()
	{
		var bridge = new Bridge();
		bridge.Linked(Ann);

		await bridge.Inbound.ReceivedAsync(Message("first", id: 1), CancellationToken.None);
		await bridge.Inbound.ReceivedAsync(Message("second", id: 2), CancellationToken.None);

		await Assert.That(bridge.Game.Spoken).IsEmpty();
		await Assert.That(bridge.Discord.Deleted).IsEmpty();
		await Assert.That(bridge.Discord.Told.Single().Text).Contains("@discord/speak 4242");
	}

	[Test]
	public async Task ARefusedLine_IsExplainedToItsAuthor()
	{
		var bridge = new Bridge();
		bridge.Linked(Ann);
		await bridge.Speakers.SetAsync(Ann, Channel, "#5:1");
		bridge.Game.Refusal = "Annabel is gagged.";

		await bridge.Inbound.ReceivedAsync(Message("hello"), CancellationToken.None);

		await Assert.That(bridge.Discord.Told.Single())
			.IsEqualTo((Ann, "Your message in #general did not reach the game: Annabel is gagged."));
		await Assert.That(bridge.Discord.Deleted).IsEmpty();
	}

	[Test]
	public async Task BotsWebhooksAndOtherChannels_AreIgnored()
	{
		var bridge = new Bridge();
		bridge.Linked(Ann);
		await bridge.Speakers.SetAsync(Ann, Channel, "#5:1");

		await bridge.Inbound.ReceivedAsync(Message("from a webhook", automated: true), CancellationToken.None);
		await bridge.Inbound.ReceivedAsync(Message("elsewhere", channel: 99), CancellationToken.None);

		await Assert.That(bridge.Game.Spoken).IsEmpty();
		await Assert.That(bridge.Discord.Told).IsEmpty();
	}

	[Test]
	public async Task DeletingAPostedMessage_TakesItsLinesOutOfRecall()
	{
		var bridge = new Bridge();
		await bridge.Pairs.SaveAsync(new DiscordMessagePair(500, Channel, [7, 8]));

		await bridge.Inbound.DeletedAsync(Channel, 501, CancellationToken.None);
		await bridge.Inbound.DeletedAsync(99, 500, CancellationToken.None);
		await Assert.That(bridge.Game.Removed).IsEmpty().Because("neither is the post");

		await bridge.Inbound.DeletedAsync(Channel, 500, CancellationToken.None);
		await Assert.That(bridge.Game.Removed).IsEquivalentTo(new long[] { 7, 8 });
		await Assert.That(bridge.Pairs.Get(500)).IsNull();
	}
}
