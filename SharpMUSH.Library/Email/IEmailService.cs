using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Email;

/// <summary>Where an email goes out: the game's SMTP server, as its <c>email_*</c> options name it.</summary>
/// <param name="Host">The SMTP server's host name.</param>
/// <param name="Port">Its port: 465 starts with TLS, any other uses STARTTLS when the server offers it.</param>
/// <param name="Username">The user name to sign in with, the password being the <c>email.password</c> secret; null sends without signing in.</param>
/// <param name="FromAddress">The address the mail is from.</param>
/// <param name="FromName">The name the mail is from.</param>
/// <param name="PerHour">How many emails may go out in an hour, counted across the game.</param>
public sealed record EmailServer(string Host, int Port, string? Username, string FromAddress, string FromName, int PerHour);

/// <summary>One plain-text email to one address.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends the game's email, what <c>@email</c> calls.</summary>
public interface IEmailService
{
	/// <summary>
	/// Sends <paramref name="message"/> through <paramref name="server"/>. Refused, with the reason, when the address is not
	/// one, when <see cref="EmailServer.PerHour"/> emails have gone out in the last hour, or when the server refuses it.
	/// </summary>
	ValueTask<Result<Success>> SendAsync(EmailServer server, EmailMessage message, CancellationToken cancellationToken = default);
}
