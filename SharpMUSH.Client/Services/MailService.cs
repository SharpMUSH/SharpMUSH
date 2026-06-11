using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side mailbox, backed by the in-game <c>@mail</c> system through <c>/api/mail</c>.
/// All operations act on the authenticated character's mail.
/// </summary>
public class MailService(IHttpClientFactory httpClientFactory, ILogger<MailService> logger)
{
	/// <summary>A mailbox row; mirrors <c>MailController.MailSummaryDto</c>.</summary>
	public record MailSummary(int Number, string From, string Subject, DateTimeOffset DateSent, bool Read, bool Urgent, string Folder);

	/// <summary>A full message; mirrors <c>MailController.MailMessageDto</c>.</summary>
	public record MailMessage(int Number, string From, string Subject, string Body, DateTimeOffset DateSent, bool Urgent, bool Read, string Folder);

	private record SendRequest(string To, string Subject, string Body, bool Urgent);

	/// <summary>Lists messages in a folder (default INBOX); empty on failure.</summary>
	public async Task<IReadOnlyList<MailSummary>> ListAsync(string folder = "INBOX")
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var rows = await http.GetFromJsonAsync<List<MailSummary>>($"api/mail?folder={Uri.EscapeDataString(folder)}");
			return rows ?? [];
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load mail folder {Folder}.", folder);
			return [];
		}
	}

	/// <summary>Lists the character's folder names.</summary>
	public async Task<IReadOnlyList<string>> FoldersAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var folders = await http.GetFromJsonAsync<List<string>>("api/mail/folders");
			return folders ?? ["INBOX"];
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load mail folders.");
			return ["INBOX"];
		}
	}

	/// <summary>Reads one message (marks it read server-side); null if not found.</summary>
	public async Task<MailMessage?> ReadAsync(string folder, int number)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<MailMessage>($"api/mail/{Uri.EscapeDataString(folder)}/{number}");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to read mail {Folder}/{Number}.", folder, number);
			return null;
		}
	}

	/// <summary>Sends mail; returns true on success.</summary>
	public async Task<bool> SendAsync(string to, string subject, string body, bool urgent)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/mail", new SendRequest(to, subject, body, urgent));
			return response.IsSuccessStatusCode;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to send mail to {To}.", to);
			return false;
		}
	}

	/// <summary>Deletes a message; returns true on success.</summary>
	public async Task<bool> DeleteAsync(string folder, int number)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync($"api/mail/{Uri.EscapeDataString(folder)}/{number}");
			return response.IsSuccessStatusCode;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to delete mail {Folder}/{Number}.", folder, number);
			return false;
		}
	}
}
