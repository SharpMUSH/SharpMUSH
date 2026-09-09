using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The stored value of a resume token is <c>version:handle:session</c>. Only that exact shape, in the
/// current version, is a binding; the session may itself contain the separator.
/// </summary>
public class NatsKvResumeTokenParseTests
{
	[Test]
	[Arguments("2:17:session-a", 17L, "session-a")]
	[Arguments("2:17:sess:with:colons", 17L, "sess:with:colons")]
	[Arguments("2:9007199254740993:s", 9007199254740993L, "s")]
	public async Task Parses_a_current_version_binding(string value, long handle, string session)
	{
		var binding = NatsKvResumeTokenStore.Parse(value);

		await Assert.That(binding.Found).IsTrue();
		await Assert.That(binding.Handle).IsEqualTo(handle);
		await Assert.That(binding.Session).IsEqualTo(session);
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("consumed")]
	[Arguments("2:17")]
	[Arguments("2:17:")]
	[Arguments("2::session")]
	[Arguments("2:seventeen:session")]
	[Arguments("1:17:session")]
	[Arguments(":17:session")]
	public async Task Rejects_anything_that_is_not_a_current_binding(string? value)
	{
		var binding = NatsKvResumeTokenStore.Parse(value);

		await Assert.That(binding.Found).IsFalse();
		await Assert.That(binding.Handle).IsEqualTo(0L);
		await Assert.That(binding.Session).IsEqualTo(string.Empty);
	}
}
