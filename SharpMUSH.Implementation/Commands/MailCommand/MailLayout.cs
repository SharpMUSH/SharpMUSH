using System.Globalization;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>How @mail draws one message.</summary>
internal static class MailLayout
{
	/// <summary>
	/// <paramref name="mail"/> in a panel titled <paramref name="title"/>: who sent it, when, its status and
	/// subject over a divider, then the message itself.
	/// </summary>
	public static async ValueTask<MString> Message(SharpMail mail, string title)
	{
		var from = (await mail.From.WithCancellation(CancellationToken.None)).Object()!.Name;
		var status = string.Join(", ", new[]
		{
			mail.Read ? "Read" : "Unread",
			mail.Urgent ? "Urgent" : null,
			mail.Forwarded ? "Forwarded" : null,
			mail.Cleared ? "Cleared" : null,
			mail.Tagged ? "Tagged" : null,
		}.OfType<string>());

		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain(title),
			ServerLayout.KeyValues(
			[
				("From", MarkupText.Plain(from)),
				("Date", MarkupText.Plain(mail.DateSent.ToString("ddd MMM dd HH:mm yyyy", CultureInfo.InvariantCulture))),
				("Status", MarkupText.Plain(status)),
				("Subject", mail.Subject),
			]),
			new Rule(),
			ServerLayout.Body(mail.Content)), 78);
	}
}
