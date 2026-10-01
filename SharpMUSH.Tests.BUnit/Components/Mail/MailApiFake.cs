using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Mail;

/// <summary>
/// The slice of <c>api/mail</c> the Mail section reads: two folders, an INBOX with two unread
/// messages and one read, a SENT folder with one. Reading a message marks it read, as the server does.
/// </summary>
public sealed class MailApiFake : HttpMessageHandler
{
	private List<MailService.MailSummary> _inbox =
	[
		new(1, "Tomas Reyes", "The ledger", DateTimeOffset.UnixEpoch, Read: false, Urgent: false, "INBOX"),
		new(2, "Wren Halloway", "Calendar of Feasts", DateTimeOffset.UnixEpoch, Read: false, Urgent: true, "INBOX"),
		new(3, "Ilsa Varn", "Lanterns", DateTimeOffset.UnixEpoch, Read: true, Urgent: false, "INBOX"),
	];

	private List<MailService.MailSummary> _sent =
	[
		new(1, "Ilsa Varn", "Re: The ledger", DateTimeOffset.UnixEpoch, Read: true, Urgent: false, "SENT"),
	];

	private string[] _folders = ["INBOX", "SENT"];

	/// <summary>The character the tab acts as first, whose mailbox the fields above are.</summary>
	public static readonly AccountAuthService.CharacterSummary First = new(313, 1, "Ilsa Varn", "PLAYER", IsActing: true);

	/// <summary>The character <see cref="SwitchCharacter"/> switches to.</summary>
	public static readonly AccountAuthService.CharacterSummary Second = new(314, 1, "Wren Halloway", "PLAYER", IsActing: true);

	/// <summary>The account session the Mail section reads its acting character from.</summary>
	public IAccountAuthState Auth { get; } = Substitute.For<IAccountAuthState>();

	/// <summary>
	/// When set, a message read is answered — against the mailbox as it stood when the request arrived —
	/// only once this completes: a read still in flight across a character switch.
	/// </summary>
	public TaskCompletionSource? HoldReads { get; set; }

	public MailApiFake() => Auth.ActiveCharacter.Returns(First);

	/// <summary>
	/// The tab switches to another character. The server binds every later request to the new one, so
	/// from here on it answers with that character's mailbox — folders INBOX and PLOTS, one unread
	/// message — and the account session announces the change.
	/// </summary>
	public void SwitchCharacter()
	{
		_folders = ["INBOX", "PLOTS"];
		_inbox = [new(1, "Mara Quill", "Second bell", DateTimeOffset.UnixEpoch, Read: false, Urgent: false, "INBOX")];
		_sent = [];
		Auth.ActiveCharacter.Returns(Second);
		Auth.ActiveCharacterChanged += Raise.Event<Action>();
	}

	public const string Body = "Meet me by the second bell.";

	/// <summary>Every list or folder request the fake has answered, to prove a change was applied without one.</summary>
	public int ListRequests { get; private set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		// Answered now, held after: a held read is the previous mailbox's, whatever happens meanwhile.
		var (response, isRead) = Answer(request);
		if (isRead && HoldReads is { } hold) await hold.Task;
		return response;
	}

	/// <summary>The response, and whether the request was a message read.</summary>
	private (HttpResponseMessage Response, bool IsRead) Answer(HttpRequestMessage request)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		var query = request.RequestUri.Query;

		if (request.Method == HttpMethod.Get && path is "api/mail/folders" or "api/mail") ListRequests++;
		if (request.Method == HttpMethod.Get && path == "api/mail/folders") return (Json(_folders), false);
		if (request.Method == HttpMethod.Get && path == "api/mail") return (Json(query.Contains("folder=SENT") ? _sent : _inbox), false);

		var parts = path.Split('/');
		if (parts is ["api", "mail", var folderName, var numberText] && int.TryParse(numberText, out var number))
		{
			var folder = folderName == "SENT" ? _sent : folderName == "INBOX" ? _inbox : null;
			var index = folder?.FindIndex(m => m.Number == number) ?? -1;
			if (folder is not null && index >= 0)
			{
				var row = folder[index];
				if (request.Method == HttpMethod.Delete)
				{
					folder.RemoveAt(index);
					return (new HttpResponseMessage(HttpStatusCode.NoContent), false);
				}

				folder[index] = row with { Read = true };
				return (Json(new MailService.MailMessage(row.Number, row.From, row.Subject, Body, row.DateSent, row.Urgent, true, folderName)), true);
			}
		}

		return (new HttpResponseMessage(HttpStatusCode.NotFound), false);
	}

	private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

	/// <summary>Registers the fake behind a real <see cref="MailService"/>, a terminal in the given state, and the rest.</summary>
	public static MailApiFake Install(TrackingBunitContext ctx, bool connected = true)
	{
		var fake = new MailApiFake();
		var client = ctx.Track(new HttpClient(fake) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(connected);
		ctx.Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(terminal)
			.AddSingleton(fake.Auth)
			.AddSingleton(new MailService(factory, fake.Auth))
			.AddSingleton<SidebarCollapseService>()
			.AddEchoLocalizer();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.AddAuthorization().SetAuthorized("headwiz");
		return fake;
	}
}
