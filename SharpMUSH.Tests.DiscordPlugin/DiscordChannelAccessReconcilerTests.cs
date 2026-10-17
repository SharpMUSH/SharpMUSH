using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordChannelAccessReconcilerTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OneFailedChannelDoesNotPreventOtherChannelsFromReconciling(bool timeout)
	{
		var links = Substitute.For<IDiscordLinkStore>();
		links.List().Returns(new[] { Link(42, 9), Link(43, 9), Link(44, 10) });
		var access = Substitute.For<IDiscordChannelGate>();
		var attempted = new List<ulong>();
		access.ReconcileAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var id = call.Arg<ulong>(); attempted.Add(id);
			return id != 42 ? Task.FromResult<string?>(null)
				: Task.FromException<string?>(timeout ? new OperationCanceledException("Synthetic per-channel timeout")
					: new InvalidOperationException("Synthetic channel failure"));
		});
		var reconciler = new DiscordChannelAccessReconciler(links, access, NullLogger<DiscordChannelAccessReconciler>.Instance);
		await reconciler.ReconcileGuildAsync(9);
		await reconciler.ReconcileGuildAsync(9);
		await Assert.That(attempted).IsEquivalentTo(new ulong[] { 42, 43, 42, 43 });
	}

	private static DiscordChannelLink Link(ulong channel, ulong guild)
		=> new(channel, guild, 8, "help", "Guild", DateTimeOffset.UtcNow, "#1:1");
}
