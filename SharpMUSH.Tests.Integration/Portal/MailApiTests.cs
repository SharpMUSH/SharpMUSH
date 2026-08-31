using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// The mailbox API (<c>api/mail</c>) the portal's Mail view reads through. It is the same mail the
/// in-game <c>@mail</c> command reads, so the two have to agree on how a message is numbered: the
/// list is numbered from 1, as <c>@mail</c> prints it, while a folder is indexed from 0 in the
/// database (which is why <c>@mail &lt;n&gt;</c> passes <c>n - 1</c>). The controller handed the
/// display number straight to the query, so reading message 1 returned message 2 and the last
/// message in a folder was a 404 — a mailbox holding one message could not be opened at all.
///
/// The test host authenticates every request as <c>#1</c> (the debug authentication handler stands
/// in for the account-session handler outside production), so these act on one shared mailbox.
/// Nothing here may assume it starts empty: each test tags its own messages and asserts against
/// those.
/// </summary>
/// <remarks>
/// <see cref="NotInParallelAttribute"/> because a folder is addressed by position: these tests share
/// one mailbox, and a delete in one test renumbers every message after it in another.
/// </remarks>
[NotInParallel]
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class MailApiTests(ServerWebAppFactory factory)
{
	private record MailSummaryDto(int Number, string From, string Subject, DateTimeOffset DateSent, bool Read, bool Urgent, string Folder);
	private record MailMessageDto(int Number, string From, string Subject, string Body, DateTimeOffset DateSent, bool Urgent, bool Read, string Folder);
	private record SendMailRequest(string To, string Subject, string Body, bool Urgent);

	/// <summary>
	/// Pinned to https: the server uses UseHttpsRedirection, and following the 307 from http→https
	/// makes HttpClient drop headers.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static string Tag(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

	/// <summary>The name of the character every request in this fixture acts as.</summary>
	private const string Self = "God";

	private async Task SendAsync(HttpClient http, string subject, string body)
	{
		var sent = await http.PostAsJsonAsync("api/mail", new SendMailRequest(Self, subject, body, false));
		await Assert.That(sent.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	private static async Task<List<MailSummaryDto>> ListAsync(HttpClient http)
		=> (await http.GetFromJsonAsync<List<MailSummaryDto>>("api/mail?folder=INBOX"))!;

	/// <summary>
	/// The headline case. The number the list reports for a message is the number that reads it —
	/// including the last one, which is where the off-by-one ran off the end and 404'd.
	/// </summary>
	[Test]
	public async Task Read_UsesTheNumberTheListReported()
	{
		var http = CreateClient();
		var subject = Tag("read");
		await SendAsync(http, subject, "The body of the message.");

		var list = await ListAsync(http);
		var mine = list.Single(m => m.Subject == subject);

		var read = await http.GetAsync($"api/mail/INBOX/{mine.Number}");
		await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var message = (await read.Content.ReadFromJsonAsync<MailMessageDto>())!;
		await Assert.That(message.Subject).IsEqualTo(subject);
		await Assert.That(message.Body).IsEqualTo("The body of the message.");
	}

	/// <summary>
	/// Where the mailbox holds more than one message the off-by-one was silent rather than a 404:
	/// every read returned its successor. Every listed number has to fetch what the list showed.
	/// </summary>
	[Test]
	public async Task Read_EachListedNumberFetchesThatMessage()
	{
		var http = CreateClient();
		var subjects = new[] { Tag("order1"), Tag("order2"), Tag("order3") };
		foreach (var subject in subjects)
		{
			await SendAsync(http, subject, $"Body of {subject}.");
		}

		var list = await ListAsync(http);

		foreach (var summary in list.Where(m => subjects.Contains(m.Subject)))
		{
			var message = await http.GetFromJsonAsync<MailMessageDto>($"api/mail/INBOX/{summary.Number}");
			await Assert.That(message!.Subject).IsEqualTo(summary.Subject);
			await Assert.That(message.Body).IsEqualTo($"Body of {summary.Subject}.");
		}
	}

	/// <summary>
	/// The endpoint runs the engine's own <c>@MAIL</c> rather than carrying a second mail
	/// implementation, so the recipient rules are the command's: <c>me</c>, a dbref and <c>*name</c>
	/// all resolve. Its own resolver was a bare player-name lookup, so none of these worked.
	/// </summary>
	[Test]
	[Arguments("me")]
	[Arguments("#1")]
	[Arguments("*God")]
	public async Task Send_ResolvesRecipientsTheWayTheCommandDoes(string recipient)
	{
		var http = CreateClient();
		var subject = Tag("to");

		var sent = await http.PostAsJsonAsync("api/mail", new SendMailRequest(recipient, subject, "Body.", false));
		await Assert.That(sent.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var list = await ListAsync(http);
		await Assert.That(list.Any(m => m.Subject == subject)).IsTrue();
	}

	/// <summary>
	/// Arguments reach the command pre-split, never spliced into a command line, so a body is stored
	/// exactly as typed: a <c>;</c> does not start a second command and softcode in it is not
	/// evaluated. This is the guarantee that makes running the command safe for web input.
	/// </summary>
	[Test]
	public async Task Send_StoresTheBodyLiterally()
	{
		var http = CreateClient();
		var subject = Tag("literal");
		const string body = "first; @pemit me=pwned [add(1,1)] 100% %r done";

		await SendAsync(http, subject, body);

		var list = await ListAsync(http);
		var mine = list.Single(m => m.Subject == subject);
		var message = await http.GetFromJsonAsync<MailMessageDto>($"api/mail/INBOX/{mine.Number}");

		await Assert.That(message!.Body).IsEqualTo(body);
	}

	/// <summary>
	/// A subject may contain a <c>/</c>: it is doubled on the way into the command's
	/// <c>[subject/]message</c> argument and comes back as one character.
	/// </summary>
	[Test]
	public async Task Send_KeepsASlashInTheSubject()
	{
		var http = CreateClient();
		var subject = $"{Tag("slash")}-and/or";

		await SendAsync(http, subject, "Body.");

		var list = await ListAsync(http);
		await Assert.That(list.Any(m => m.Subject == subject)).IsTrue();
	}

	/// <summary>The urgent flag has to survive the hop through the command's switches.</summary>
	[Test]
	public async Task Send_MarksUrgentMailUrgent()
	{
		var http = CreateClient();
		var subject = Tag("urgent");

		var sent = await http.PostAsJsonAsync("api/mail", new SendMailRequest(Self, subject, "Body.", true));
		await Assert.That(sent.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var list = await ListAsync(http);
		await Assert.That(list.Single(m => m.Subject == subject).Urgent).IsTrue();
	}

	/// <summary>
	/// The command reports an unmatched name to the character rather than in a return value, so the
	/// endpoint infers the failure from an empty delivered-recipient list.
	/// </summary>
	[Test]
	public async Task Send_UnknownRecipientIsNotFound()
	{
		var http = CreateClient();

		var sent = await http.PostAsJsonAsync("api/mail",
			new SendMailRequest("NoSuchCharacterAnywhere", Tag("miss"), "Body.", false));

		await Assert.That(sent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	/// <summary>
	/// Delete read the same shifted index, so deleting the message you were looking at removed its
	/// neighbour instead — silent data loss rather than a 404.
	/// </summary>
	[Test]
	public async Task Delete_RemovesTheMessageAtThatNumber()
	{
		var http = CreateClient();
		var keep = Tag("keep");
		var drop = Tag("drop");
		await SendAsync(http, keep, "Kept.");
		await SendAsync(http, drop, "Dropped.");

		var before = await ListAsync(http);
		var target = before.Single(m => m.Subject == drop);

		var deleted = await http.DeleteAsync($"api/mail/INBOX/{target.Number}");
		await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var after = await ListAsync(http);
		await Assert.That(after.Any(m => m.Subject == drop)).IsFalse();
		await Assert.That(after.Any(m => m.Subject == keep)).IsTrue();
	}
}
