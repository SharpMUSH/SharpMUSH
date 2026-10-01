using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side mailbox, backed by the in-game <c>@mail</c> system through <c>/api/mail</c>.
/// All operations act on the authenticated character's mail.
/// </summary>
/// <remarks>
/// Every call answers with <see cref="ApiResult{T}"/>. The server says why a send was refused —
/// "No such character: Bob", "Subject may be at most N characters." — and a <see langword="bool"/>
/// dropped that, leaving the page to guess at the recipient.
/// </remarks>
public class MailService(IHttpClientFactory httpClientFactory, IAccountAuthState accountAuth)
{
	/// <summary>A mailbox row; mirrors <c>MailController.MailSummaryDto</c>.</summary>
	public record MailSummary(int Number, string From, string Subject, DateTimeOffset DateSent, bool Read, bool Urgent, string Folder);

	/// <summary>A full message; mirrors <c>MailController.MailMessageDto</c>.</summary>
	public record MailMessage(int Number, string From, string Subject, string Body, DateTimeOffset DateSent, bool Urgent, bool Read, string Folder);

	private record SendRequest(string To, string Subject, string Body, bool Urgent);

	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>Lists messages in a folder (default INBOX).</summary>
	public Task<ApiResult<IReadOnlyList<MailSummary>>> ListAsync(string folder = "INBOX") =>
		Client.GetApiAsync<IReadOnlyList<MailSummary>>(
			$"api/mail?folder={Uri.EscapeDataString(folder)}", "The server returned no mailbox.");

	/// <summary>Lists the character's folder names.</summary>
	public Task<ApiResult<IReadOnlyList<string>>> FoldersAsync() =>
		Client.GetApiAsync<IReadOnlyList<string>>("api/mail/folders", "The server returned no folder list.");

	/// <summary>
	/// What changed in the mailbox, for the Mail section's sidebar beside the page that made the change.
	/// </summary>
	/// <param name="Folder">The folder that changed.</param>
	/// <param name="MarkedRead">True when the only change is one unread message in it becoming read, so
	/// a count can be adjusted where it stands; false when the folder's contents may have changed.</param>
	public sealed record MailChange(string Folder, bool MarkedRead);

	/// <summary>
	/// Raised after a read that changed something, a send or a delete lands — unless the tab switched
	/// character while it was in flight. The change was to the previous character's mailbox, which
	/// nothing shows any more, and applied to the new one's it would be wrong.
	/// </summary>
	public event Action<MailChange>? Changed;

	/// <summary>The mailbox a request acts on: the server binds it to the tab's acting character.</summary>
	private (int, long)? Mailbox => accountAuth.ActiveCharacter is { } acting ? (acting.DbrefNumber, acting.CreationTime) : null;

	private void Report((int, long)? mailbox, MailChange change)
	{
		if (Mailbox == mailbox) Changed?.Invoke(change);
	}

	/// <summary>
	/// Reads one message, which marks it read server-side. <paramref name="wasUnread"/> is what the
	/// caller knows of it: true (the list showed it unread) or false (already read) let the sidebar
	/// adjust or ignore; null (a direct link) makes it re-read the folder.
	/// </summary>
	public async Task<ApiResult<MailMessage>> ReadAsync(string folder, int number, bool? wasUnread = null)
	{
		// A row clicked again before its first read answers sends a second read that also says "unread".
		// One message changes state, so only the first read to succeed reports it; a read that fails
		// leaves the report to the next one. Folder and number name a message only within one character's
		// mailbox, so the mailbox is part of what is claimed.
		var mailbox = Mailbox;
		var key = (mailbox, folder, number);
		if (wasUnread is true)
		{
			lock (_unreadInFlight) _unreadInFlight.Add(key);
		}

		var result = await Client.GetApiAsync<MailMessage>(
			$"api/mail/{Uri.EscapeDataString(folder)}/{number}", "The server returned no message.");
		if (result is MailMessage)
		{
			switch (wasUnread)
			{
				case null:
					Report(mailbox, new MailChange(folder, MarkedRead: false));
					break;
				case true when Claim(key):
					Report(mailbox, new MailChange(folder, MarkedRead: true));
					break;
			}
		}

		return result;
	}

	/// <summary>
	/// Unread messages being read and not yet reported read. Locked: WASM runs on one thread, but the
	/// service does not assume it.
	/// </summary>
	private readonly HashSet<((int, long)? Mailbox, string Folder, int Number)> _unreadInFlight = [];

	private bool Claim(((int, long)?, string, int) key)
	{
		lock (_unreadInFlight) return _unreadInFlight.Remove(key);
	}

	private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);

	/// <summary>
	/// Holds a message body for the compose page (a forward's quoted text) and returns the id to
	/// hand it over by. A body does not travel in the address, where a long one hits URL limits and
	/// lands in history; a reload loses it, as it would lose anything typed.
	/// </summary>
	public string StageDraft(string body)
	{
		var id = Guid.NewGuid().ToString("N");
		_drafts[id] = body;
		return id;
	}

	/// <summary>The staged body, once: taking it removes it.</summary>
	public Found<string> TakeDraft(string id) =>
		_drafts.Remove(id, out var body) ? body : new NotFound();

	public async Task<ApiResult<Success>> SendAsync(string to, string subject, string body, bool urgent)
	{
		var mailbox = Mailbox;
		var result = await Client.PostApiAsync("api/mail", new SendRequest(to, subject, body, urgent));
		if (result is Success) Report(mailbox, new MailChange("SENT", MarkedRead: false));
		return result;
	}

	public async Task<ApiResult<Success>> DeleteAsync(string folder, int number)
	{
		var mailbox = Mailbox;
		var result = await Client.DeleteApiAsync($"api/mail/{Uri.EscapeDataString(folder)}/{number}");
		if (result is Success) Report(mailbox, new MailChange(folder, MarkedRead: false));
		return result;
	}
}
