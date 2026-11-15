using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The acting character's inbox, as the navigation and the Mail section's sidebar show it: how many messages in it are
/// unread, and each new message as it arrives, for a notice.
/// </summary>
/// <remarks>
/// <para>The count is read from <c>api/mail</c> when the character's terminal connects, when the tab switches
/// character, and after a change the portal made (<see cref="MailService.Changed"/>: one message read takes one off
/// where it stands). New mail is the <c>comm-feed</c> package's <c>mail.new</c> push, read off the command
/// terminal's OOB store, which every page of the portal keeps connected while a character is active; each one reads
/// the count again and raises <see cref="Arrived"/>.</para>
/// <para>It assumes, as the OOB store does, a single-threaded dispatcher (Blazor WASM).</para>
/// </remarks>
public sealed class MailWatch : IDisposable
{
	/// <summary>The OOB package the <c>comm-feed</c> package pushes to a player for each @mail they receive.</summary>
	public const string NewMailPackage = "mail.new";

	private const string Inbox = "INBOX";

	/// <summary>A message that has just arrived, as <c>mail.new</c> describes it.</summary>
	/// <param name="Number">Its number in the inbox, as <c>@mail</c> prints it.</param>
	public sealed record NewMail(int Number, string From, string Subject, bool Urgent);

	private readonly MailService _mail;
	private readonly ITerminalService _terminal;
	private readonly IAccountAuthState _auth;

	/// <summary>Only the latest read may write: reads land in any order.</summary>
	private long _ticket;

	public MailWatch(MailService mail, ITerminalService terminal, IAccountAuthState auth)
	{
		(_mail, _terminal, _auth) = (mail, terminal, auth);
		_terminal.ConnectionStateChanged += OnConnectionChanged;
		_terminal.OobChannels.ChannelUpdated += OnOob;
		_auth.ActiveCharacterChanged += OnActingChanged;
		_mail.Changed += OnMailChanged;
		if (_terminal.IsConnected) _ = RefreshAsync();
	}

	/// <summary>Unread messages in the inbox, or null while there is no character or the count has not been read.</summary>
	public int? InboxUnread { get; private set; }

	/// <summary>Raised when <see cref="InboxUnread"/> changes.</summary>
	public event Action? Changed;

	/// <summary>Raised for each new message, after the count is read again.</summary>
	public event Action<NewMail>? Arrived;

	/// <summary>Reads the count again.</summary>
	public async Task RefreshAsync()
	{
		var ticket = ++_ticket;
		if (!_terminal.IsConnected)
		{
			Set(null);
			return;
		}

		var rows = await _mail.ListAsync(Inbox);
		if (ticket != _ticket) return;
		Set(rows is IReadOnlyList<MailService.MailSummary> list ? list.Count(r => !r.Read) : InboxUnread);
	}

	private void Set(int? unread)
	{
		if (unread == InboxUnread) return;
		InboxUnread = unread;
		Changed?.Invoke();
	}

	private void OnConnectionChanged(bool connected) => _ = RefreshAsync();

	/// <summary>The count on screen is the previous character's; it goes at once, not when the new one is read.</summary>
	private void OnActingChanged()
	{
		Set(null);
		_ = RefreshAsync();
	}

	private void OnMailChanged(MailService.MailChange change)
	{
		switch (change)
		{
			case { MarkedRead: true } when string.Equals(change.Folder, Inbox, StringComparison.OrdinalIgnoreCase):
				if (InboxUnread is > 0) Set(InboxUnread - 1);
				break;
			case { MarkedRead: true }:
			case { Folder: MailService.SentList }:
				break;
			default:
				_ = RefreshAsync();
				break;
		}
	}

	private void OnOob(string package)
	{
		if (package != NewMailPackage || _terminal.OobChannels.Get(package) is not { } json) return;
		var arrived = Parse(json);
		_ = ArrivedAsync(arrived);
	}

	private async Task ArrivedAsync(NewMail? arrived)
	{
		await RefreshAsync();
		if (arrived is not null) Arrived?.Invoke(arrived);
	}

	/// <summary>
	/// A <c>mail.new</c> payload: <c>{"v":2,"number":3,"from":"Alice","fromObjid":"#5:…","subject":"…","urgent":false}</c>.
	/// Null when it is not one; it still says the inbox changed.
	/// </summary>
	public static NewMail? Parse(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number
				|| !number.TryGetInt32(out var n))
				return null;
			return new NewMail(n, Text(root, "from"), Text(root, "subject"),
				root.TryGetProperty("urgent", out var urgent) && urgent.ValueKind == JsonValueKind.True);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string Text(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

	public void Dispose()
	{
		_terminal.ConnectionStateChanged -= OnConnectionChanged;
		_terminal.OobChannels.ChannelUpdated -= OnOob;
		_auth.ActiveCharacterChanged -= OnActingChanged;
		_mail.Changed -= OnMailChanged;
	}
}
