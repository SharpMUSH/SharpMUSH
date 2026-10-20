using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SharpMUSH.Client.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Changing a mounted view's identity invalidates both its visible state and pending reads.</summary>
public class WikiViewIdentityTests : TrackingBunitContext
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
	private readonly IdentityApi _api = new();
	private IRenderedComponent<IdentityHost>? _host;
	private Task? _initialRead;

	public WikiViewIdentityTests()
	{
		Services.AddMudServices();
		AddAuthorization().SetAuthorized("reader");
		JSInterop.Mode = JSRuntimeMode.Loose;
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<WikiMarkdigPipeline>()
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
	}

	[Test]
	[Arguments("expected")]
	[Arguments("namespace")]
	[Arguments("locale")]
	public async Task ChangedIdentityClearsCompletedTitleAndBodyBeforeReplacementAnswers(string change)
	{
		var old = _api.Enqueue();
		var cut = RenderView();
		try
		{
			await AssertRequest(old, "main", "en", "old-id");
			await cut.InvokeAsync(() => old.Complete(Page("old-id", "OLD_TITLE", "OLD_BODY")));
			cut.WaitForState(() => cut.Markup.Contains("OLD_BODY", StringComparison.Ordinal), Deadline);
			await Assert.That(cut.Markup).Contains("OLD_TITLE");
			var current = _api.Enqueue();
			ChangeIdentity(cut, change);
			await AssertChangedRequest(current, change);
			await AssertOldAbsent(cut);
			await Assert.That(cut.FindAll(".wiki-article-body")).IsEmpty();
			await cut.InvokeAsync(() => current.Complete(ChangedPage(change)));
			cut.WaitForState(() => cut.Markup.Contains("NEW_BODY", StringComparison.Ordinal), Deadline);
			await Assert.That(cut.Markup).Contains("NEW_TITLE");
			await AssertOldAbsent(cut);
		}
		finally { await DisposeAndJoin(cut); }
	}

	[Test]
	public async Task ChangedExpectedIdClearsUnsavedEditorDraftBeforeReplacementAnswers()
	{
		var old = _api.Enqueue();
		var cut = RenderView(WikiView.WikiMode.Edit);
		try
		{
			await AssertRequest(old, "main", "en", "old-id");
			await cut.InvokeAsync(() => old.Complete(Page("old-id", "OLD_TITLE", "OLD_BODY")));
			cut.WaitForState(() => cut.FindAll(".wiki-edit-title").Count == 1, Deadline);
			await cut.Find(".wiki-edit-title").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "OLD_UNSAVED_TITLE" });
			await cut.Find(".wiki-edit-textarea").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "OLD_UNSAVED_BODY" });
			await Assert.That(cut.Find(".wiki-edit-title").GetAttribute("value")).IsEqualTo("OLD_UNSAVED_TITLE");
			await Assert.That(cut.Find(".wiki-edit-textarea").GetAttribute("value")).IsEqualTo("OLD_UNSAVED_BODY");
			var current = _api.Enqueue();
			ChangeIdentity(cut, "expected");
			await AssertChangedRequest(current, "expected");
			await AssertOldAbsent(cut);
			await Assert.That(cut.FindAll(".wiki-edit")).IsEmpty();
			await cut.InvokeAsync(() => current.Complete(Page("new-id", "NEW_TITLE", "NEW_BODY")));
			cut.WaitForState(() => cut.FindAll(".wiki-edit-title").Count == 1, Deadline);
			await Assert.That(cut.Find(".wiki-edit-title").GetAttribute("value")).IsEqualTo("NEW_TITLE");
			await Assert.That(cut.Find(".wiki-edit-textarea").GetAttribute("value")).IsEqualTo("NEW_BODY");
			await AssertOldAbsent(cut);
		}
		finally { await DisposeAndJoin(cut); }
	}

	[Test]
	[Arguments("expected")]
	[Arguments("namespace")]
	[Arguments("locale")]
	public async Task DelayedOldResponseCannotReplaceCompletedNewIdentity(string change)
	{
		var old = _api.Enqueue();
		var cut = RenderView();
		try
		{
			await AssertRequest(old, "main", "en", "old-id");
			var current = _api.Enqueue();
			ChangeIdentity(cut, change);
			await AssertChangedRequest(current, change);
			await cut.InvokeAsync(() => current.Complete(ChangedPage(change)));
			cut.WaitForState(() => cut.Markup.Contains("NEW_BODY", StringComparison.Ordinal), Deadline);
			await cut.InvokeAsync(() => old.Complete(Page("old-id", "OLD_TITLE", "OLD_BODY")));
			await old.Consumed.Task.WaitAsync(Deadline);
			await cut.Instance.InitialReadCompleted.Task.WaitAsync(Deadline);
			ChangeIdentity(cut, change);
			await Assert.That(_api.ArticleRequests).IsEqualTo(2);
			await Assert.That(cut.Markup).Contains("NEW_TITLE");
			await Assert.That(cut.Markup).Contains("NEW_BODY");
			await AssertOldAbsent(cut);
		}
		finally { await DisposeAndJoin(cut); }
	}

	[Test]
	public async Task ActualUnmountWhileReadIsPendingPreventsLateArticlePublication()
	{
		var articles = new List<string?>();
		var old = _api.Enqueue();
		var cut = RenderView(onArticle: article => articles.Add(article?.Title));
		try
		{
			await AssertRequest(old, "main", "en", "old-id");
			var initialRead = cut.Instance.InitialReadCompleted.Task;
			await UnmountAsync();
			await Assert.That(_host!.FindAll(".wiki-article, .wiki-edit")).IsEmpty();
			var auth = TestAccountAuth.Of(Services);
			await Renderer.Dispatcher.InvokeAsync(() =>
			{
				auth.AuthStateChanged += Raise.Event<Action>();
				auth.ActiveCharacterChanged += Raise.Event<Action>();
				old.Complete(Page("old-id", "OLD_TITLE", "OLD_BODY"));
			});
			await old.Consumed.Task.WaitAsync(Deadline);
			await initialRead.WaitAsync(Deadline);
			await Assert.That(_host!.Markup).DoesNotContain("OLD_");
			await Assert.That(articles).IsEmpty();
			await Assert.That(_api.ArticleRequests).IsEqualTo(1);
		}
		finally { await DisposeAndJoin(cut); }
	}

	private IRenderedComponent<ObservedWikiView> RenderView(WikiView.WikiMode mode = WikiView.WikiMode.View,
		Action<WikiArticle?>? onArticle = null)
	{
		_host = Render<IdentityHost>(p => p.Add(x => x.Mode, mode)
			.Add(x => x.OnArticle, EventCallback.Factory.Create(this, onArticle ?? (_ => { }))));
		var cut = _host.FindComponent<ObservedWikiView>();
		_initialRead = cut.Instance.InitialReadCompleted.Task;
		return cut;
	}

	private Task UnmountAsync() => Renderer.Dispatcher.InvokeAsync(() =>
		_host!.Render(p => p.Add(x => x.Visible, false)));

	/// <summary>Observes only completion; all rendering and behavior remain inherited from WikiView.</summary>
	public sealed class ObservedWikiView : WikiView
	{
		public TaskCompletionSource InitialReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		protected override async Task OnInitializedAsync()
		{
			try { await base.OnInitializedAsync(); }
			finally { InitialReadCompleted.TrySetResult(); }
		}
	}

	public sealed class IdentityHost : ComponentBase
	{
		[Parameter] public bool Visible { get; set; } = true;
		[Parameter] public WikiView.WikiMode Mode { get; set; }
		[Parameter] public EventCallback<WikiArticle?> OnArticle { get; set; }

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			// Keep the existing locale selector provider mounted while removing the actual view.
			builder.OpenComponent<MudHarness>(0);
			builder.AddAttribute(1, "ChildContent", (RenderFragment)(child =>
			{
				if (!Visible) return;
				child.OpenComponent<ObservedWikiView>(0);
				child.AddAttribute(1, nameof(WikiView.Slug), "guide");
				child.AddAttribute(2, nameof(WikiView.Namespace), "main");
				child.AddAttribute(3, nameof(WikiView.Locale), "en");
				child.AddAttribute(4, nameof(WikiView.ExpectedPageId), "old-id");
				child.AddAttribute(5, nameof(WikiView.Mode), Mode);
				child.AddAttribute(6, nameof(WikiView.OnArticle), OnArticle);
				child.CloseComponent();
			}));
			builder.CloseComponent();
		}
	}

	private static void ChangeIdentity(IRenderedComponent<ObservedWikiView> cut, string change) => cut.Render(p => p
		.Add(x => x.Namespace, change == "namespace" ? "help" : "main")
		.Add(x => x.Locale, change == "locale" ? "fr-CA" : "en")
		.Add(x => x.ExpectedPageId, change == "expected" ? "new-id" : "old-id"));

	private static Task AssertChangedRequest(PendingRead read, string change) => AssertRequest(read,
		change == "namespace" ? "help" : "main", change == "locale" ? "fr-CA" : "en",
		change == "expected" ? "new-id" : "old-id");

	private static async Task AssertRequest(PendingRead read, string ns, string locale, string expectedId)
	{
		var uri = await read.Started.Task.WaitAsync(Deadline);
		await Assert.That(uri.AbsolutePath).IsEqualTo($"/api/wiki/ns/{ns}/guide");
		var query = QueryHelpers.ParseQuery(uri.Query);
		await Assert.That(query["lang"].ToString()).IsEqualTo(locale);
		await Assert.That(query["expectedPageId"].ToString()).IsEqualTo(expectedId);
		await Assert.That(query["expectedPageId"].Count).IsEqualTo(1);
	}

	private static async Task AssertOldAbsent(IRenderedComponent<ObservedWikiView> cut) =>
		await Assert.That(cut.Markup).DoesNotContain("OLD_");

	private async Task DisposeAndJoin(IRenderedComponent<ObservedWikiView> cut)
	{
		var initialRead = _initialRead!;
		await UnmountAsync();
		// Release transport work and join the observed initial component lifecycle before client teardown.
		await Renderer.Dispatcher.InvokeAsync(() => _api.ReleaseAll());
		await _api.JoinAsync();
		await initialRead.WaitAsync(Deadline);
		await Renderer.Dispatcher.InvokeAsync(() => { });
	}

	private static WikiPageDto Page(string id, string title, string body) => new(id, "guide", title, "main",
		body, $"<p>{body}</p>", body, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, false, 1, [], true)
	{
		Locale = "en", RequestedLocale = "en", AvailableLocales = ["en"]
	};

	private static WikiPageDto ChangedPage(string change) => Page(change == "expected" ? "new-id" : "old-id", "NEW_TITLE", "NEW_BODY") with
	{
		Namespace = change == "namespace" ? "help" : "main",
		Locale = change == "locale" ? "fr-CA" : "en",
		RequestedLocale = change == "locale" ? "fr-CA" : "en",
		AvailableLocales = change == "locale" ? ["fr-CA"] : ["en"]
	};

	private sealed class PendingRead
	{
		// Synchronous continuations allow release on the renderer dispatcher to drain the held read.
		public TaskCompletionSource<Uri> Started { get; } = new();
		public TaskCompletionSource<HttpResponseMessage> Answer { get; } = new();
		public TaskCompletionSource Consumed { get; } = new();

		public void Complete(WikiPageDto page)
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ObservedContent(page, Consumed)
			};
			if (!Answer.TrySetResult(response)) response.Dispose();
		}
	}

	private sealed class ObservedContent(WikiPageDto page, TaskCompletionSource consumed)
		: StringContent(System.Text.Json.JsonSerializer.Serialize(page, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
			System.Text.Encoding.UTF8, "application/json")
	{
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (disposing) consumed.TrySetResult();
		}
	}

	private sealed class IdentityApi : HttpMessageHandler
	{
		private readonly List<PendingRead> _reads = [];
		public int ArticleRequests { get; private set; }
		public PendingRead Enqueue()
		{
			var read = new PendingRead();
			_reads.Add(read);
			return read;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			if (uri.AbsolutePath.StartsWith("/api/wiki/ns/", StringComparison.Ordinal))
			{
				var read = _reads[ArticleRequests++];
				read.Started.SetResult(uri);
				return read.Answer.Task;
			}
			if (uri.AbsolutePath == "/api/wiki/category-names")
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new Dictionary<string, string>()) });
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		public void ReleaseAll()
		{
			foreach (var read in _reads.Where(x => x.Started.Task.IsCompletedSuccessfully))
				if (!read.Answer.Task.IsCompleted) read.Complete(Page("cleanup", "Cleanup", "Cleanup"));
		}

		public Task JoinAsync() => Task.WhenAll(_reads.Where(x => x.Started.Task.IsCompletedSuccessfully)
			.Select(x => x.Consumed.Task)).WaitAsync(Deadline);
	}
}
