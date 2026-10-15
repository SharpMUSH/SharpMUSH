using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Outbox;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordRelayTests
{
	private const ulong Channel = 4242;
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
	private static readonly SecretAuthor Admin = new("#1:1", "God");

	private sealed class FixedClock : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class MemoryLinkStore : IDiscordLinkStore
	{
		private readonly ConcurrentDictionary<ulong, DiscordChannelLink> _links = new();

		public TaskCompletionSource<DiscordChannelLink> Faulted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public IReadOnlyList<DiscordChannelLink> List() => [.. _links.Values];

		public DiscordChannelLink? Get(ulong channelId) => _links.GetValueOrDefault(channelId);

		public ValueTask SaveAsync(DiscordChannelLink link, CancellationToken cancellationToken = default)
		{
			_links[link.ChannelId] = link;
			if (link.Fault is not null)
			{
				Faulted.TrySetResult(link);
			}

			return ValueTask.CompletedTask;
		}

		public ValueTask<bool> RemoveAsync(ulong channelId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(_links.TryRemove(channelId, out _));
	}

	private sealed class MemorySecrets : ISecretStore
	{
		public ConcurrentDictionary<string, string> Values { get; } = new();

		public IReadOnlyList<SecretDefinition> Definitions => [];

		public ValueTask<FoundResult<string>> GetAsync(string name, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<FoundResult<string>>(Values.TryGetValue(name, out var value) ? value : new NotFound());

		public ValueTask<Result<SecretStatus>> SetAsync(string name, string value, SecretAuthor author,
			CancellationToken cancellationToken = default)
		{
			Values[name] = value;
			return ValueTask.FromResult<Result<SecretStatus>>(Status(name));
		}

		public ValueTask<Found<SecretStatus>> ClearAsync(string name, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<Found<SecretStatus>>(Values.TryRemove(name, out _) ? Status(name) : new NotFound());

		public ValueTask<IReadOnlyList<SecretStatus>> ListAsync(CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<IReadOnlyList<string>> HostsAsync(string name, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<Result<SecretStatus>> SetHostsAsync(string name, IReadOnlyList<string> hosts,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<SecretSnapshot> SnapshotAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask RestoreAsync(SecretSnapshot snapshot, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		private static SecretStatus Status(string name) => new(name, null, default, null, true, Now, Admin, null, false);
	}

	/// <summary>Hands out the webhooks queued in it, one per call, then refuses.</summary>
	private sealed class QueuedWebhooks(params WebhookCreation[] results) : IDiscordWebhooks
	{
		private readonly Queue<WebhookCreation> _results = new(results);

		public int Calls { get; private set; }

		public Task<WebhookCreation> CreateWebhookAsync(ulong channelId, CancellationToken cancellationToken)
		{
			Calls++;
			return Task.FromResult(_results.TryDequeue(out var result)
				? result
				: new WebhookCreation(new Error<string>("The bot may not make webhooks there.")));
		}
	}

	/// <summary>Holds every request until <see cref="Release"/>, then hands out a new webhook for each.</summary>
	private sealed class HeldWebhooks : IDiscordWebhooks
	{
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _calls;

		public int Calls => _calls;
		public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public void Release() => _released.TrySetResult();

		public async Task<WebhookCreation> CreateWebhookAsync(ulong channelId, CancellationToken cancellationToken)
		{
			var call = (ulong)Interlocked.Increment(ref _calls);
			Asked.TrySetResult();
			await _released.Task.WaitAsync(cancellationToken);
			return Webhook(call, $"token-{call}");
		}
	}

	/// <summary>Records posts; a webhook id in <see cref="Gone"/> fails as Discord does for a deleted webhook.</summary>
	private sealed class FakePoster(ulong webhookId) : IDiscordLinkPoster
	{
		private readonly List<DiscordPost> _posts = [];

		public HashSet<ulong> Gone { get; } = [];
		public bool Refusing { get; set; }
		public TaskCompletionSource<DiscordPost> Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public bool Deleted { get; private set; }
		public ulong WebhookId { get; set; } = webhookId;

		public Task<ulong> PostAsync(DiscordPost post, CancellationToken cancellationToken)
		{
			if (Gone.Contains(WebhookId))
			{
				throw new DiscordLinkException(DiscordFailures.UnknownWebhook, "The channel's webhook was deleted or reset.");
			}

			if (Refusing)
			{
				throw new InvalidOperationException("Cannot send an empty message.");
			}

			lock (_posts)
			{
				_posts.Add(post);
				Posted.TrySetResult(post);
				return Task.FromResult((ulong)_posts.Count);
			}
		}

		public Task DeleteWebhookAsync(CancellationToken cancellationToken)
		{
			Deleted = true;
			return Task.CompletedTask;
		}

		public void Dispose()
		{
		}
	}

	private sealed class Harness
	{
		public MemoryLinkStore Store { get; } = new();
		public MemorySecrets Secrets { get; } = new();
		public List<FakePoster> Posters { get; } = [];
		public HashSet<ulong> Gone { get; } = [];

		public bool Refusing { get; init; }

		public DiscordRelay Relay(params WebhookCreation[] webhooks) => Relay(new QueuedWebhooks(webhooks));

		public DiscordRelay Relay(IDiscordWebhooks webhooks)
			=> new(Store, Secrets, webhooks, link =>
				{
					var poster = new FakePoster(link.WebhookId);
					poster.Gone.UnionWith(Gone);
					poster.Refusing = Refusing;
					Posters.Add(poster);
					return poster;
				},
				new DiscordOutboxOptions(Gather: TimeSpan.Zero), new FixedClock(), NullLogger<DiscordRelay>.Instance);
	}

	private static WebhookCreation Webhook(ulong id, string token)
		=> new(new CreatedWebhook(id, token, 900, "Guild", "general"));

	private static FeedLine Line(string kind, string key, string text, string audience = "", DBRef[]? to = null)
		=> new(new SharpFeedMessage(1, kind, key, Now, new DBRef(5), "Alice", new DBRef(5), "Alice", null, "", "say",
			MString.Plain(text), Audience: audience), [], true, "", to ?? []);

	[Test, Timeout(30_000)]
	public async Task ALinkedChannel_GetsItsFeedsLines_AsTheSpeaker(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		var relay = harness.Relay(Webhook(1, "token-1"));
		await relay.StartAsync(cancellationToken);

		var linked = await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);
		await relay.LineSentAsync(Line("discord", "4242", "Hello, Discord."), cancellationToken);
		var post = await harness.Posters.Single().Posted.Task.WaitAsync(cancellationToken);
		await relay.StopAsync(cancellationToken);

		await Assert.That(linked is DiscordChannelLink { WebhookId: 1 }).IsTrue();
		await Assert.That(harness.Secrets.Values["discord.webhook.4242"]).IsEqualTo("token-1");
		await Assert.That(harness.Store.Get(Channel)?.ChannelName).IsEqualTo("general");
		await Assert.That($"{post.Speaker}: {post.Content}").IsEqualTo("Alice: Hello, Discord.");
	}

	[Test, Timeout(30_000)]
	public async Task OtherLines_AreNotPosted(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		var relay = harness.Relay(Webhook(1, "token-1"));
		await relay.StartAsync(cancellationToken);
		await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);

		await relay.LineSentAsync(Line("radio", "4242", "another kind"), cancellationToken);
		await relay.LineSentAsync(Line("discord", "999", "a channel not linked"), cancellationToken);
		await relay.LineSentAsync(Line("discord", "4242", "for some members only", audience: "staff"), cancellationToken);
		await relay.LineSentAsync(Line("discord", "4242", "for two people", to: [new DBRef(7), new DBRef(8)]),
			cancellationToken);
		await relay.LineSentAsync(Line("discord", "4242", "this one"), cancellationToken);
		var post = await harness.Posters.Single().Posted.Task.WaitAsync(cancellationToken);
		await relay.StopAsync(cancellationToken);

		await Assert.That(post.Content).IsEqualTo("this one");
	}

	[Test]
	public async Task ARefusal_LinksNothing(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		var relay = harness.Relay(new WebhookCreation(new Error<string>("The bot lacks Manage Webhooks in #general.")));

		var linked = await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);

		await Assert.That(linked is Error<string> { Value: "The bot lacks Manage Webhooks in #general." }).IsTrue();
		await Assert.That(harness.Store.List()).IsEmpty();
		await Assert.That(harness.Secrets.Values).IsEmpty();
	}

	[Test]
	public async Task LinkingTwice_IsRefused(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		var relay = harness.Relay(Webhook(1, "token-1"), Webhook(2, "token-2"));
		await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);

		var again = await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);
		await relay.StopAsync(cancellationToken);

		await Assert.That(again is Error<string> { Value: "#general is already linked." }).IsTrue();
		await Assert.That(harness.Secrets.Values["discord.webhook.4242"]).IsEqualTo("token-1");
	}

	[Test, Timeout(30_000)]
	public async Task ADeletedWebhook_IsReplaced_AndTheLineStillGoesOut(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		harness.Gone.Add(1);
		var relay = harness.Relay(Webhook(1, "token-1"), Webhook(2, "token-2"));
		await relay.StartAsync(cancellationToken);
		await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);

		await relay.LineSentAsync(Line("discord", "4242", "after the webhook went"), cancellationToken);
		var poster = harness.Posters.Single();
		var post = await poster.Posted.Task.WaitAsync(cancellationToken);
		await relay.StopAsync(cancellationToken);

		await Assert.That(post.Content).IsEqualTo("after the webhook went");
		await Assert.That(poster.WebhookId).IsEqualTo(2UL);
		await Assert.That(harness.Store.Get(Channel)?.WebhookId).IsEqualTo(2UL);
		await Assert.That(harness.Store.Get(Channel)?.Fault).IsNull();
		await Assert.That(harness.Secrets.Values["discord.webhook.4242"]).IsEqualTo("token-2");
	}

	[Test, Timeout(30_000)]
	public async Task AWebhookThatCannotBeReplaced_StopsTheLink_UntilResumed(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		harness.Gone.Add(1);
		var relay = harness.Relay(Webhook(1, "token-1"));
		await relay.StartAsync(cancellationToken);
		await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);

		await relay.LineSentAsync(Line("discord", "4242", "held"), cancellationToken);
		var faulted = await harness.Store.Faulted.Task.WaitAsync(cancellationToken);
		var stopped = relay.Links().Single();

		var poster = harness.Posters.Single();
		poster.Gone.Clear();
		var resumed = await relay.ResumeAsync(Channel, cancellationToken);
		var post = await poster.Posted.Task.WaitAsync(cancellationToken);
		await relay.StopAsync(cancellationToken);

		await Assert.That(faulted.Fault?.Code).IsEqualTo(DiscordFailures.UnknownWebhook);
		await Assert.That(faulted.Fault?.Reason).IsEqualTo(
			"The channel's webhook was deleted or reset. A new webhook could not be made: The bot may not make webhooks there.");
		await Assert.That(stopped.State).IsEqualTo(DiscordLinkState.Faulted);
		await Assert.That(resumed is DiscordChannelLink { Fault: null }).IsTrue();
		await Assert.That(harness.Store.Get(Channel)?.Fault).IsNull();
		await Assert.That(post.Content).IsEqualTo("held");
	}

	[Test, Timeout(30_000)]
	public async Task ALinkStoppedBeforeARestart_StaysStopped(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		var fault = new DiscordLinkFault(50013, "The bot lacks a permission it needs in the channel.", Now);
		await harness.Store.SaveAsync(new DiscordChannelLink(Channel, 900, 1, "general", "Guild", Now, "#1:1", fault),
			cancellationToken);
		var relay = harness.Relay();
		await relay.StartAsync(cancellationToken);

		await relay.LineSentAsync(Line("discord", "4242", "waits"), cancellationToken);
		var status = relay.Links().Single();
		await relay.StopAsync(cancellationToken);

		await Assert.That(status.State).IsEqualTo(DiscordLinkState.Faulted);
		await Assert.That(status.Depth).IsEqualTo(1);
		await Assert.That(harness.Posters.Single().Posted.Task.IsCompleted).IsFalse();
	}

	[Test, Timeout(30_000)]
	public async Task TwoLinksOfOneChannelAtOnce_MakeOneWebhook(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		var webhooks = new HeldWebhooks();
		var relay = harness.Relay(webhooks);

		var first = relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);
		await webhooks.Asked.Task.WaitAsync(cancellationToken);
		var second = relay.LinkAsync(Channel, "#2:1", Admin, cancellationToken);
		webhooks.Release();
		var results = await Task.WhenAll(first, second);
		await relay.StopAsync(cancellationToken);

		await Assert.That(results[0] is DiscordChannelLink { WebhookId: 1 }).IsTrue();
		await Assert.That(results[1] is Error<string> { Value: "#general is already linked." }).IsTrue();
		await Assert.That(webhooks.Calls).IsEqualTo(1);
		await Assert.That(harness.Secrets.Values["discord.webhook.4242"]).IsEqualTo("token-1");
	}

	[Test, Timeout(30_000)]
	public async Task TooManyRefusedPosts_StopTheLink_AcrossARestart(CancellationToken cancellationToken)
	{
		var harness = new Harness { Refusing = true };
		var relay = harness.Relay(Webhook(1, "token-1"));
		await relay.StartAsync(cancellationToken);
		await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);

		for (var i = 0; i < new DiscordOutboxOptions().RejectionsBeforeFault; i++)
		{
			await relay.LineSentAsync(Line("discord", "4242", $"refused {i}"), cancellationToken);
		}

		var faulted = await harness.Store.Faulted.Task.WaitAsync(cancellationToken);
		await relay.StopAsync(cancellationToken);

		await Assert.That(faulted.Fault?.Reason).IsEqualTo("Discord refused 5 messages within a minute.");
	}

	[Test]
	public async Task Unlinking_DeletesTheWebhook_AndForgetsItsToken(CancellationToken cancellationToken)
	{
		var harness = new Harness();
		var relay = harness.Relay(Webhook(1, "token-1"));
		await relay.StartAsync(cancellationToken);
		await relay.LinkAsync(Channel, "#1:1", Admin, cancellationToken);

		var unlinked = await relay.UnlinkAsync(Channel, cancellationToken);
		var again = await relay.UnlinkAsync(Channel, cancellationToken);
		await relay.StopAsync(cancellationToken);

		await Assert.That(unlinked is DiscordChannelLink { ChannelId: Channel }).IsTrue();
		await Assert.That(again is NotFound).IsTrue();
		await Assert.That(harness.Posters.Single().Deleted).IsTrue();
		await Assert.That(harness.Secrets.Values).IsEmpty();
		await Assert.That(harness.Store.List()).IsEmpty();
	}
}
