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
public class MailService(IHttpClientFactory httpClientFactory)
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
	/// Raised after a read, a send or a delete lands: each can change a folder's contents or its
	/// unread count, which the Mail section's sidebar shows beside the page that made the change.
	/// </summary>
	public event Action? Changed;

	/// <summary>Reads one message, which marks it read server-side.</summary>
	public async Task<ApiResult<MailMessage>> ReadAsync(string folder, int number)
	{
		var result = await Client.GetApiAsync<MailMessage>(
			$"api/mail/{Uri.EscapeDataString(folder)}/{number}", "The server returned no message.");
		if (result is MailMessage) Changed?.Invoke();
		return result;
	}

	public async Task<ApiResult<Success>> SendAsync(string to, string subject, string body, bool urgent)
	{
		var result = await Client.PostApiAsync("api/mail", new SendRequest(to, subject, body, urgent));
		if (result is Success) Changed?.Invoke();
		return result;
	}

	public async Task<ApiResult<Success>> DeleteAsync(string folder, int number)
	{
		var result = await Client.DeleteApiAsync($"api/mail/{Uri.EscapeDataString(folder)}/{number}");
		if (result is Success) Changed?.Invoke();
		return result;
	}
}
