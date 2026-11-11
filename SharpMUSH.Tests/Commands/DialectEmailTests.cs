using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Commands;

/// <summary>TinyMUX's <c>@email</c>, against an SMTP server of the test's own.</summary>
public class DialectEmailTests : ServerTestBase
{
	private static IDisposable Email(FakeSmtpServer smtp) => TestOptionsOverride.Scope(options => options with
	{
		Compatibility = options.Compatibility with { Dialect = "mux" },
		Net = options.Net with { EmailServer = "127.0.0.1", EmailPort = (uint)smtp.Port, EmailFrom = "game@example.com" }
	});

	private bool GodHeard(string text)
		=> Notifications.For(WebAppFactoryArg.ExecutorDBRef).Any(line => line.Contains(text, StringComparison.Ordinal));

	[Test]
	public async Task SendsTheEvaluatedBodyWithItsSubject()
	{
		await using var smtp = new FakeSmtpServer();
		var address = $"{TestIsolationHelpers.GenerateUniqueName("mail").ToLowerInvariant()}@example.com";
		using (Email(smtp))
			await Cmd($"@email {address}/Greetings=Two and two is [add(2,2)].");

		var data = await smtp.WaitForAsync(address);
		await Assert.That(data).IsNotNull();
		await Assert.That(data!).Contains("Subject: Greetings");
		await Assert.That(data).Contains("Two and two is 4.");
		for (var waited = 0; waited < 100 && !GodHeard($"@email: sent to {address}."); waited++)
			await Task.Delay(50);
		await Assert.That(GodHeard($"@email: sent to {address}.")).IsTrue();
	}

	[Test]
	public async Task IsOffUntilTheServerIsNamed()
	{
		string result;
		using (TestOptionsOverride.Scope(options => options with { Compatibility = options.Compatibility with { Dialect = "mux" } }))
			result = await Cmd($"@email someone@example.com=hello");

		await Assert.That(result).Contains("@email is not set up");
	}
}
