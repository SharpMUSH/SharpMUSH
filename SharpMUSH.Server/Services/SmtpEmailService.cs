using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Email;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// <see cref="IEmailService"/> over SMTP: one connection per email, signed in when the server names a user, and no
/// more than <see cref="EmailServer.PerHour"/> sent in any hour.
/// </summary>
public sealed class SmtpEmailService(ISecretStore secrets, TimeProvider clock, ILogger<SmtpEmailService> logger) : IEmailService
{
	/// <summary>How long one email may take, connecting included.</summary>
	private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

	private readonly Queue<DateTimeOffset> _sent = new();
	private readonly Lock _gate = new();

	public async ValueTask<Result<Success>> SendAsync(EmailServer server, EmailMessage message,
		CancellationToken cancellationToken = default)
	{
		if (!MailboxAddress.TryParse(message.To, out var to) || to.Address.IndexOf('@') <= 0)
			return new Error<string>($"{message.To} is not an email address.");
		if (!MailboxAddress.TryParse(server.FromAddress, out var from))
			return new Error<string>("email_from is not an email address.");
		if (!TryReserve(server.PerHour))
			return new Error<string>("Too many emails sent in the last hour.");

		using var mail = new MimeMessage();
		mail.From.Add(new MailboxAddress(server.FromName, from.Address));
		mail.To.Add(to);
		mail.Subject = message.Subject;
		mail.Body = new TextPart("plain") { Text = message.Body };

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(SendTimeout);
		using var client = new SmtpClient();
		try
		{
			var security = server.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
			await client.ConnectAsync(server.Host, server.Port, security, timeout.Token);
			if (server.Username is { Length: > 0 } username)
			{
				if (await secrets.GetAsync(SecretDefinition.EmailPassword.Name, timeout.Token) is not string password)
					return new Error<string>("The email.password secret is not set.");
				await client.AuthenticateAsync(username, password, timeout.Token);
			}

			await client.SendAsync(mail, timeout.Token);
		}
		catch (Exception failure) when (failure is not OutOfMemoryException)
		{
			logger.LogWarning(failure, "Email to {Address} through {Host}:{Port} failed", to.Address, server.Host, server.Port);
			return new Error<string>(failure is OperationCanceledException
				? "The mail server did not answer in time."
				: $"The mail server refused it: {failure.Message}");
		}

		// The email is sent; a failed goodbye does not make it unsent.
		try
		{
			await client.DisconnectAsync(true, timeout.Token);
		}
		catch (Exception failure) when (failure is not OutOfMemoryException)
		{
			logger.LogDebug(failure, "Closing the connection to {Host}:{Port} failed after sending", server.Host, server.Port);
		}

		return new Success();
	}

	/// <summary>Counts one email against the hour, or refuses when <paramref name="perHour"/> have gone out in it.</summary>
	private bool TryReserve(int perHour)
	{
		var now = clock.GetUtcNow();
		lock (_gate)
		{
			while (_sent.TryPeek(out var oldest) && now - oldest >= TimeSpan.FromHours(1))
				_sent.Dequeue();
			if (_sent.Count >= perHour)
				return false;
			_sent.Enqueue(now);
			return true;
		}
	}
}
