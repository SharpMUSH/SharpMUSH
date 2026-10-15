using System.Net;
using Discord;
using Discord.Net;
using NSubstitute;
using SharpMUSH.Plugins.Discord.Outbox;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordFailuresTests
{
	private static HttpException Http(HttpStatusCode status, int? code = null)
		=> new(status, Substitute.For<IRequest>(), (DiscordErrorCode?)code, "reason");

	[Test]
	public async Task ADeletedWebhook_BreaksTheLink()
	{
		await Assert.That(DiscordFailures.Classify(Http(HttpStatusCode.NotFound, 10015)).Value)
			.IsEqualTo(new LinkBroken(10015, "The channel's webhook was deleted or reset."));
	}

	[Test]
	public async Task AMissingPermission_BreaksTheLink()
	{
		await Assert.That(DiscordFailures.Classify(Http(HttpStatusCode.Forbidden, 50013)).Value).IsTypeOf<LinkBroken>();
	}

	[Test]
	public async Task AServerError_IsTriedAgain()
	{
		await Assert.That(DiscordFailures.Classify(Http(HttpStatusCode.BadGateway)).Value).IsTypeOf<Transient>();
	}

	[Test]
	public async Task ANetworkError_IsTriedAgain()
	{
		await Assert.That(DiscordFailures.Classify(new HttpRequestException("reset")).Value).IsTypeOf<Transient>();
	}

	[Test]
	public async Task AnInvalidBody_RefusesOnlyThatMessage()
	{
		await Assert.That(DiscordFailures.Classify(Http(HttpStatusCode.BadRequest, 50035)).Value).IsTypeOf<Rejected>();
	}
}
