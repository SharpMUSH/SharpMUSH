using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Email;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Secrets;

namespace SharpMUSH.Tests.Services;

/// <summary>@email's sender, against an SMTP server of the test's own.</summary>
public class SmtpEmailServiceTests
{
	private static SmtpEmailService Service(TimeProvider clock)
		=> new(FixedSecrets.With("unused", "unused"), clock, NullLogger<SmtpEmailService>.Instance);

	private static EmailServer Server(FakeSmtpServer smtp, int perHour = 50)
		=> new("127.0.0.1", smtp.Port, null, "game@example.com", "Test Game", perHour);

	[Test]
	public async Task SendsThePlainTextEmail()
	{
		await using var smtp = new FakeSmtpServer();
		var sent = await Service(TimeProvider.System).SendAsync(Server(smtp), new EmailMessage("player@example.com", "Hello", "Line one\nLine two"));

		await Assert.That(sent is Success).IsTrue();
		var data = await smtp.WaitForAsync("Line two");
		await Assert.That(data).IsNotNull();
		await Assert.That(data!).Contains("Subject: Hello");
		await Assert.That(data).Contains("From: Test Game <game@example.com>");
		await Assert.That(data).Contains("To: player@example.com");
	}

	[Test]
	public async Task RefusesWhatIsNotAnAddress()
	{
		await using var smtp = new FakeSmtpServer();
		var sent = await Service(TimeProvider.System).SendAsync(Server(smtp), new EmailMessage("nobody", "Hi", "Body"));

		await Assert.That(sent is Error<string> { Value: var reason } && reason.Contains("not an email address")).IsTrue();
		await Assert.That(smtp.Received).IsEmpty();
	}

	[Test]
	public async Task SendsNoMoreThanPerHourInAnHour()
	{
		await using var smtp = new FakeSmtpServer();
		var clock = new SecretStoreTests.FixedClock(DateTimeOffset.UnixEpoch);
		var service = Service(clock);
		var server = Server(smtp, perHour: 2);
		var message = new EmailMessage("player@example.com", "Hi", "Body");

		await Assert.That(await service.SendAsync(server, message) is Success).IsTrue();
		await Assert.That(await service.SendAsync(server, message) is Success).IsTrue();
		await Assert.That(await service.SendAsync(server, message) is Error<string>).IsTrue();
	}

	[Test]
	public async Task NamesTheMissingPasswordWhenItMustSignIn()
	{
		await using var smtp = new FakeSmtpServer();
		var sent = await Service(TimeProvider.System).SendAsync(Server(smtp) with { Username = "game" },
			new EmailMessage("player@example.com", "Hi", "Body"));

		await Assert.That(sent is Error<string> { Value: var reason } && reason.Contains("email.password")).IsTrue();
	}
}
