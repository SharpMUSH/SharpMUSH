using System.Net;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Tests.Services.Webhooks;

/// <summary>
/// The pieces of a delivery a receiver checks or a wizard types: the signature (to the Standard Webhooks
/// specification, so its libraries verify it), the event patterns and the addresses a webhook may reach.
/// </summary>
public class WebhookSigningTests
{
	[Test]
	public async Task TheSignatureMatchesTheStandardWebhooksExample()
	{
		// The worked example in the Standard Webhooks specification.
		var signature = WebhookSignature.Sign("whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw", "msg_p5jXN8AQM9LWM0D4loKWxJek",
			1614265330, "{\"test\": 2432232314}");

		await Assert.That(signature).IsEqualTo("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=");
	}

	[Test]
	public async Task ANewSecretIsThirtyTwoRandomBytes()
	{
		var first = WebhookSignature.NewSecret();
		var second = WebhookSignature.NewSecret();

		await Assert.That(first).StartsWith(WebhookSignature.SecretPrefix);
		await Assert.That(Convert.FromBase64String(first[WebhookSignature.SecretPrefix.Length..]).Length).IsEqualTo(32);
		await Assert.That(first).IsNotEqualTo(second);
	}

	[Test]
	[Arguments("PLAYER`CONNECT", "PLAYER`CONNECT", true)]
	[Arguments("player`connect", "PLAYER`CONNECT", true)]
	[Arguments("PLAYER`*", "PLAYER`DISCONNECT", true)]
	[Arguments("PLAYER`*", "PAGE`MESSAGE", false)]
	[Arguments("*`MESSAGE", "CHANNEL`MESSAGE", true)]
	[Arguments("*", "SOCKET`CONNECT", true)]
	[Arguments("SOCKET`?ONNECT", "SOCKET`CONNECT", true)]
	[Arguments("SOCKET`?ONNECT", "SOCKET`DISCONNECT", false)]
	[Arguments("OBJECT", "OBJECT`MOVE", false)]
	public async Task PatternsMatchEventNamesWithoutRegardToCase(string pattern, string eventName, bool expected)
	{
		await Assert.That(WebhookEventPattern.Matches(pattern, eventName)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("127.0.0.1", false)]
	[Arguments("10.1.2.3", false)]
	[Arguments("172.16.0.1", false)]
	[Arguments("172.32.0.1", true)]
	[Arguments("192.168.1.1", false)]
	[Arguments("169.254.169.254", false)]
	[Arguments("100.64.0.1", false)]
	[Arguments("0.0.0.0", false)]
	[Arguments("::1", false)]
	[Arguments("fd00::1", false)]
	[Arguments("fe80::1", false)]
	[Arguments("::ffff:10.0.0.1", false)]
	[Arguments("8.8.8.8", true)]
	[Arguments("2606:4700:4700::1111", true)]
	public async Task OnlyPublicAddressesArePublic(string address, bool expected)
	{
		await Assert.That(WebhookAddress.IsPublic(IPAddress.Parse(address))).IsEqualTo(expected);
	}

	[Test]
	public async Task RetriesWaitFiveTimesLongerEachTimeUpToTheCeiling()
	{
		var options = new WebhookOptions { FirstRetryDelay = TimeSpan.FromSeconds(5), MaxRetryDelay = TimeSpan.FromHours(2) };

		TimeSpan[] waits = [.. Enumerable.Range(2, 8).Select(options.RetryDelay)];

		await Assert.That(waits).IsEquivalentTo(new[]
		{
			TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(125), TimeSpan.FromSeconds(625),
			TimeSpan.FromSeconds(3125), TimeSpan.FromHours(2), TimeSpan.FromHours(2), TimeSpan.FromHours(2),
		});
	}
}
