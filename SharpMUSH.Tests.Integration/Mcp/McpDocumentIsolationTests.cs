using NSubstitute;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Mcp;

namespace SharpMUSH.Tests.Integration.Mcp;

public class McpDocumentIsolationTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static void Identity(HttpContextAccessor accessor, int key, long incarnation = 100, string scheme = MushBasicAuthenticationHandler.SchemeName)
		=> accessor.HttpContext = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity([
			new Claim("character_key", key.ToString()), new Claim("character_creation_time", incarnation.ToString()),
			new Claim(ClaimTypes.NameIdentifier, "fixture-account")], scheme))
		};

	[Test]
	public async Task Handles_AreOwnedByAuthenticatedCharacterIncarnation()
	{
		var context = new HttpContextAccessor();
		Identity(context, 1);
		var store = new McpDocumentStore(Options.Create(new McpOptions()), context, TimeProvider.System);
		var id = store.Open("private text");
		Identity(context, 2);
		await Assert.That(store.TryGet(id, out _)).IsFalse();
		await Assert.That(store.Close(id)).IsFalse();
		Identity(context, 1, 101);
		await Assert.That(store.TryGet(id, out _)).IsFalse();
		Identity(context, 1);
		await Assert.That(store.TryGet(id, out var text)).IsTrue();
		await Assert.That(text).IsEqualTo("private text");
		await Assert.That(store.Close(id)).IsTrue();
		await Assert.That(store.Close(id)).IsFalse();
	}

	[Test]
	public async Task MissingOrWrongAuthentication_FailsClosed()
	{
		var context = new HttpContextAccessor();
		var store = new McpDocumentStore(Options.Create(new McpOptions()), context, TimeProvider.System);
		await Assert.That(() => store.Open("text")).Throws<UnauthorizedAccessException>();
		Identity(context, 1, scheme: "DebugAuth");
		await Assert.That(() => store.Open("text")).Throws<UnauthorizedAccessException>();
	}

	[Test]
	public async Task FiveMiBDocuments_AreAcceptedAndUtf8OversizeIsRejected()
	{
		var context = new HttpContextAccessor();
		Identity(context, 1);
		var store = new McpDocumentStore(Options.Create(new McpOptions()), context, TimeProvider.System);
		var text = new string('x', 5 * 1024 * 1024);
		var id = store.Open(text);
		await Assert.That(store.TryGet(id, out var returned)).IsTrue();
		await Assert.That(returned.Length).IsEqualTo(text.Length);
		await Assert.That(() => store.Open(new string('é', text.Length / 2 + 1))).Throws<ArgumentException>();
	}

	[Test]
	public async Task Quotas_RejectWithoutEvictingOtherPrincipalsAndCloseReleasesBudget()
	{
		var context = new HttpContextAccessor();
		Identity(context, 1);
		var options = new McpOptions { MaxDocumentsPerPrincipal = 1, MaxStoredBytes = 8, MaxBytesPerPrincipal = 8, MaxDocumentBytes = 8 };
		var store = new McpDocumentStore(Options.Create(options), context, TimeProvider.System);
		var first = store.Open("1234");
		await Assert.That(() => store.Open("a")).Throws<ArgumentException>();
		Identity(context, 2);
		var second = store.Open("5678");
		Identity(context, 3);
		await Assert.That(() => store.Open("a")).Throws<ArgumentException>();
		Identity(context, 1);
		await Assert.That(store.TryGet(first, out _)).IsTrue();
		await Assert.That(store.Close(first)).IsTrue();
		Identity(context, 3);
		await Assert.That(store.Open("a")).IsNotNull();
		Identity(context, 2);
		await Assert.That(store.TryGet(second, out _)).IsTrue();
	}

	[Test]
	public async Task IdleExpiry_ReleasesQuotaAndForeignReadsCannotKeepDocumentAlive()
	{
		var context = new HttpContextAccessor();
		Identity(context, 1);
		var clock = new Clock();
		var store = new McpDocumentStore(Options.Create(new McpOptions { DocumentIdleTimeout = TimeSpan.FromMinutes(1), MaxDocumentsPerPrincipal = 1 }), context, clock);
		var id = store.Open("text");
		clock.Now += TimeSpan.FromSeconds(30);
		Identity(context, 2);
		await Assert.That(store.TryGet(id, out _)).IsFalse();
		clock.Now += TimeSpan.FromSeconds(31);
		Identity(context, 1);
		await Assert.That(store.TryGet(id, out _)).IsFalse();
		await Assert.That(store.Open("next")).IsNotNull();
	}
	[Test]
	public async Task ConcurrentFiveMiBAdmission_DoesNotExceedAggregateBudget()
	{
		var context = new HttpContextAccessor();
		Identity(context, 1);
		var bytes = 5 * 1024 * 1024;
		var store = new McpDocumentStore(Options.Create(new McpOptions
		{
			MaxDocumentsPerPrincipal = 4, MaxBytesPerPrincipal = 4L * bytes,
			MaxStoredBytes = 4L * bytes
		}), context, TimeProvider.System);
		var text = new string('x', bytes);
		var opens = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
		{
			try { return store.Open(text); }
			catch (ArgumentException) { return null; }
		}));
		var ids = (await Task.WhenAll(opens).WaitAsync(TimeSpan.FromSeconds(10)))
			.Where(id => id is not null).Select(id => id!).ToArray();
		await Assert.That(ids.Length).IsEqualTo(4);
		foreach (var id in ids)
		{
			await Assert.That(store.TryGet(id, out var returned)).IsTrue();
			await Assert.That(returned.Length).IsEqualTo(bytes);
			await Assert.That(store.Close(id)).IsTrue();
		}
		await Assert.That(store.Open(text)).IsNotNull();
	}

	[Test]
	public async Task InlineBudget_IsAppliedBeforeTheAnalyzerRuns()
	{
		var context = new HttpContextAccessor();
		Identity(context, 1);
		var analyzer = NSubstitute.Substitute.For<SharpMUSH.CodeAnalysis.IMushCodeAnalyzer>();
		var store = new McpDocumentStore(Options.Create(new McpOptions { MaxDocumentBytes = 8 }), context, TimeProvider.System);
		using var executor = new McpAnalysisExecutor(Options.Create(new McpOptions()));
		var tools = new MushTools(new SharpMUSH.Tests.Integration.AnalysisTransport.FixedAnalysisSessions(analyzer), context, store, executor);
		await Assert.That(async () => { await tools.Validate(code: "123456789"); }).Throws<ArgumentException>();
		await Assert.That(analyzer.ReceivedCalls().Any()).IsFalse();
	}

}
