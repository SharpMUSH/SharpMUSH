using System.Net;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Services;

public class OnboardingServiceTests
{
	private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
	}
	private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://example.test/") };
	}
	private sealed class Info(IHttpClientFactory factory) : ServerInfoService(factory)
	{
		public override Task<OnboardingPresentation?> OnboardingAsync() => Task.FromResult<OnboardingPresentation?>(new("game-1", "", "", "", "minimalist", "", null, null, null));
	}
	private sealed class Storage : IJSRuntime
	{
		public Dictionary<string, string> Values { get; } = [];
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		{
			var key = (string)args![0]!;
			if (identifier.EndsWith(".setItem", StringComparison.Ordinal)) Values[key] = (string)args[1]!;
			if (identifier.EndsWith(".removeItem", StringComparison.Ordinal)) Values.Remove(key);
			object? value = identifier.EndsWith(".getItem", StringComparison.Ordinal) ? Values.GetValueOrDefault(key) : default(TValue);
			return ValueTask.FromResult((TValue)value!);
		}
	}
	private static HttpResponseMessage Reply(OnboardingProgress progress) => new(HttpStatusCode.OK) { Content = JsonContent.Create(progress) };
	private static IAccountAuthState Auth(string? token = null)
	{
		var auth = Substitute.For<IAccountAuthState>();
		auth.AccountSessionToken.Returns(token);
		auth.IsLoggedIn.Returns(token is not null);
		return auth;
	}

	[Test]
	public async Task ReloadRetainsDismissal()
	{
		using var handler = new Handler(_ => throw new InvalidOperationException("Anonymous progress must stay local"));
		var factory = new Factory(handler);
		var storage = new Storage();
		using var first = new OnboardingService(factory, Auth(), storage, new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		await first.LoadAsync(GuideJourney.Player);
		await first.ChoosePathAsync(GuidePath.Discord);
		await first.DismissAsync();
		using var second = new OnboardingService(factory, Auth(), storage, new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		await second.LoadAsync(GuideJourney.Player);
		await Assert.That(second.Progress.Dismissed).IsTrue();
		await Assert.That(second.Progress.Active).IsFalse();
	}

	[Test]
	public async Task LateResponseCannotCrossAccount()
	{
		var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var handler = new Handler(request => request.Headers.Authorization?.Parameter == "A"
			? delayed.Task : Task.FromResult(Reply(new() { Path = GuidePath.Mush })));
		var factory = new Factory(handler);
		var auth = Auth("A");
		using var guide = new OnboardingService(factory, auth, new Storage(), new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		var old = guide.LoadAsync(GuideJourney.Player);
		auth.AccountSessionToken.Returns("B");
		auth.AuthStateChanged += Raise.Event<Action>();
		await guide.LoadAsync(GuideJourney.Player);
		delayed.SetResult(Reply(new() { Path = GuidePath.Discord, Active = true }));
		await old;
		await Assert.That(guide.Progress.Path).IsEqualTo(GuidePath.Mush);
		await Assert.That(guide.Progress.Active).IsFalse();
	}

	[Test]
	public async Task ConflictRequiresReload()
	{
		using var handler = new Handler(request => Task.FromResult(request.Method == HttpMethod.Get
			? Reply(new() { Revision = 4, Dismissed = true }) : new(HttpStatusCode.Conflict)));
		var factory = new Factory(handler);
		using var guide = new OnboardingService(factory, Auth("A"), new Storage(), new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		await guide.LoadAsync(GuideJourney.Player);
		await guide.ChoosePathAsync(GuidePath.Discord);
		await Assert.That(guide.Conflict).IsTrue();
		await Assert.That(guide.Progress.Dismissed).IsTrue();
		await Assert.That(guide.Progress.Revision).IsEqualTo(4);
	}
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AnonymousProgressRequiresAnExplicitChoice(bool accept)
	{
		var stored = new OnboardingProgress { Revision = 3, Path = GuidePath.Discord, Active = true, CompletedStepIds = ["place"] };
		var storage = new Storage();
		storage.Values["sharpmush.guide.v1:game-1:Player"] = System.Text.Json.JsonSerializer.Serialize(stored);
		var writes = 0;
		using var handler = new Handler(async request =>
		{
			if (request.Method == HttpMethod.Get) return Reply(new());
			writes++;
			var save = (await request.Content!.ReadFromJsonAsync<SaveOnboardingRequest>())!;
			return Reply(save.Progress with { Revision = save.ExpectedRevision + 1 });
		});
		var factory = new Factory(handler);
		using var guide = new OnboardingService(factory, Auth("A"), storage, new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		await guide.LoadAsync(GuideJourney.Player);
		await Assert.That(writes).IsEqualTo(0);
		await Assert.That(guide.HasAnonymousContinuation).IsTrue();
		await Assert.That(guide.Progress.CompletedStepIds).IsEmpty();
		if (accept) await guide.AcceptAnonymousContinuationAsync();
		else await guide.DeclineAnonymousContinuationAsync();
		await Assert.That(writes).IsEqualTo(1);
		await Assert.That(guide.Progress.CompletedStepIds.Contains("place")).IsEqualTo(accept);
		await Assert.That(guide.HasAnonymousContinuation).IsFalse();
		await Assert.That(storage.Values["sharpmush.guide.v1:game-1:Player"]).IsEqualTo(System.Text.Json.JsonSerializer.Serialize(stored));
	}

	[Test]
	public async Task FutureLocalProgressIsNotOverwritten()
	{
		var stored = System.Text.Json.JsonSerializer.Serialize(new OnboardingProgress { SchemaVersion = 99, Revision = 5 });
		var storage = new Storage();
		storage.Values["sharpmush.guide.v1:game-1:Player"] = stored;
		using var handler = new Handler(_ => throw new InvalidOperationException("Anonymous progress must stay local"));
		var factory = new Factory(handler);
		using var guide = new OnboardingService(factory, Auth(), storage, new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		await guide.LoadAsync(GuideJourney.Player);
		await guide.ChoosePathAsync(GuidePath.Discord);
		await Assert.That(guide.Ready).IsFalse();
		await Assert.That(storage.Values["sharpmush.guide.v1:game-1:Player"]).IsEqualTo(stored);
	}

	[Test]
	public async Task AuthorityRefreshRestoresActiveGuideWithoutNavigation()
	{
		var reads = 0;
		using var handler = new Handler(_ =>
		{
			reads++;
			return Task.FromResult(Reply(new() { Path = GuidePath.Discord, Active = true, CurrentStepId = "world" }));
		});
		var factory = new Factory(handler);
		var auth = Auth("A");
		using var guide = new OnboardingService(factory, auth, new Storage(), new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		await guide.LoadAsync(GuideJourney.Player);
		auth.AuthStateChanged += Raise.Event<Action>();
		await guide.AuthorityRefresh;
		await Assert.That(reads).IsEqualTo(2);
		await Assert.That(guide.Ready).IsTrue();
		await Assert.That(guide.Progress.CurrentStepId).IsEqualTo("world");
		await Assert.That(guide.Progress.Active).IsTrue();
	}

	[Test]
	public async Task VeteranTourFinishesWithoutCompletingNoviceLessons()
	{
		using var handler = new Handler(_ => throw new InvalidOperationException("Anonymous progress stays local"));
		var factory = new Factory(handler);
		using var guide = new OnboardingService(factory, Auth(), new Storage(), new Info(factory), SharpMUSH.Tests.BUnit.Resources.PortalLocalizer.Create());
		await guide.LoadAsync(GuideJourney.Player);
		await guide.ChoosePathAsync(GuidePath.Mush);
		foreach (var lesson in GuideCatalog.Player(GuidePath.Mush)) await guide.CompleteStepAsync(lesson.Id);
		await Assert.That(guide.Progress.Active).IsFalse();
		await Assert.That(guide.Progress.CompletedStepIds).IsEquivalentTo(new[] { "place", "sign-in", "world", "continue" });
		await Assert.That(guide.Progress.SkippedStepIds).IsEmpty();
	}

}
