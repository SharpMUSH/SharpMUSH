using System.Net;
using System.Text;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Reading an unread message tells the Mail sidebar to take one off its unread count. A row clicked
/// again before the first read answers is the same message becoming read once, so it is reported once.
/// </summary>
public class MailServiceReadTests : IDisposable
{
	private const string Message = """
		{"number":3,"from":"Ilsa Varn","subject":"Lamps","body":"Fourth bell.","dateSent":"2026-09-30T12:00:00Z","urgent":false,"read":true,"folder":"INBOX"}
		""";

	/// <summary>Answers a message read once <see cref="Gate"/> opens; the first <see cref="Failures"/> reads fail.</summary>
	private sealed class Handler : HttpMessageHandler
	{
		public TaskCompletionSource Gate { get; } = new();

		/// <summary>When not empty, each request waits on the next of these instead of <see cref="Gate"/>.</summary>
		public Queue<TaskCompletionSource> EachRequest { get; } = new();

		private int _failures;

		public int Failures { set => _failures = value; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			TaskCompletionSource gate;
			lock (EachRequest) gate = EachRequest.Count > 0 ? EachRequest.Dequeue() : Gate;
			await gate.Task;
			return Interlocked.Decrement(ref _failures) >= 0
				? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Message, Encoding.UTF8, "application/json") };
		}
	}

	private readonly List<HttpClient> _clients = [];

	public void Dispose()
	{
		foreach (var client in _clients)
		{
			client.Dispose();
		}
	}

	private static readonly AccountAuthService.CharacterSummary Ilsa = new(313, 1, "Ilsa Varn", "PLAYER", IsActing: true);
	private static readonly AccountAuthService.CharacterSummary Wren = new(314, 1, "Wren Halloway", "PLAYER", IsActing: true);

	private readonly IAccountAuthState _auth = Substitute.For<IAccountAuthState>();

	private (MailService Mail, Handler Handler, List<MailService.MailChange> Changes) Build()
	{
		var handler = new Handler();
		var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		_clients.Add(http);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(http);
		var mail = new MailService(factory, _auth);
		var changes = new List<MailService.MailChange>();
		mail.Changed += change => { lock (changes) changes.Add(change); };
		return (mail, handler, changes);
	}

	[Test]
	public async Task TwoReadsInFlight_OfOneUnreadMessage_ReportItReadOnce()
	{
		var (mail, handler, changes) = Build();
		var first = mail.ReadAsync("INBOX", 3, wasUnread: true);
		var second = mail.ReadAsync("INBOX", 3, wasUnread: true);
		handler.Gate.SetResult();
		await Task.WhenAll(first, second);

		await Assert.That(changes.Count(c => c.MarkedRead)).IsEqualTo(1)
			.Because("one message changed state, so the unread count drops by one");
	}

	[Test]
	public async Task WhenTheFirstReadFails_TheOneThatSucceedsReportsIt()
	{
		var (mail, handler, changes) = Build();
		handler.Failures = 1;
		var first = mail.ReadAsync("INBOX", 3, wasUnread: true);
		var second = mail.ReadAsync("INBOX", 3, wasUnread: true);
		handler.Gate.SetResult();
		await Task.WhenAll(first, second);

		await Assert.That(changes.Count(c => c.MarkedRead)).IsEqualTo(1);
	}

	[Test]
	public async Task ReadingItAgainLater_AsUnread_ReportsItAgain()
	{
		var (mail, handler, changes) = Build();
		handler.Gate.SetResult();
		await mail.ReadAsync("INBOX", 3, wasUnread: true);
		await mail.ReadAsync("INBOX", 3, wasUnread: true);

		await Assert.That(changes.Count(c => c.MarkedRead)).IsEqualTo(2)
			.Because("only reads in flight together are the same change; a later read is the caller's word");
	}

	/// <summary>
	/// Two characters' unread messages that share a folder and number are two different messages. The
	/// in-flight claim was keyed on folder and number alone, so the previous character's read, landing
	/// first, took the claim — and was rightly not reported — and the new character's read then found
	/// nothing to claim and was not reported either.
	/// </summary>
	[Test]
	public async Task TheSameNumberInTwoCharactersMailboxes_IsClaimedSeparately()
	{
		var (mail, handler, changes) = Build();
		var (ilsasRead, wrensRead) = (new TaskCompletionSource(), new TaskCompletionSource());
		handler.EachRequest.Enqueue(ilsasRead);
		handler.EachRequest.Enqueue(wrensRead);

		_auth.ActiveCharacter.Returns(Ilsa);
		var first = mail.ReadAsync("INBOX", 3, wasUnread: true);
		_auth.ActiveCharacter.Returns(Wren);
		var second = mail.ReadAsync("INBOX", 3, wasUnread: true);

		ilsasRead.SetResult();
		await first;
		wrensRead.SetResult();
		await second;

		await Assert.That(changes.Count(c => c.MarkedRead)).IsEqualTo(1)
			.Because("Wren's message became read in the mailbox on screen; Ilsa's is not on screen");
	}
}
