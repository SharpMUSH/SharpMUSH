using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Client.Services;

/// <summary>Optional learning state. Every asynchronous result belongs to one captured session and journey.</summary>
public sealed class OnboardingService : IDisposable
{
	private readonly IHttpClientFactory _http;
	private readonly IAccountAuthState _auth;
	private readonly IJSRuntime _js;
	private readonly ServerInfoService _info;
	private readonly IStringLocalizer<SharedResource> _text;
	private readonly SemaphoreSlim _writes = new(1);
	private long _generation;
	private bool _disposed;
	private bool _started;
	public Task AuthorityRefresh { get; private set; } = Task.CompletedTask;
	private string? _key;
	private OnboardingProgress? _anonymous;
	private GuideJourney _journey;
	public GuideJourney Journey => _journey;
	private string? Session => _auth.IsLoggedIn && !string.IsNullOrWhiteSpace(_auth.AccountSessionToken) ? _auth.AccountSessionToken : null;
	public OnboardingProgress Progress { get; private set; } = new();
	public OnboardingPresentation? Presentation { get; private set; }
	public bool Ready { get; private set; }
	public bool Loading { get; private set; }
	public bool Conflict { get; private set; }
	public string? Error { get; private set; }
	public bool HasAnonymousContinuation => _auth.IsLoggedIn && _anonymous is { Revision: > 0 } anonymous
		&& GuideStepIds.IsValid(_journey, anonymous, anonymous.Revision) && !Progress.AnonymousContinuationHandled;
	public event Action? Changed;

	public OnboardingService(IHttpClientFactory http, IAccountAuthState auth, IJSRuntime js, ServerInfoService info, IStringLocalizer<SharedResource> text)
	{
		_http = http; _auth = auth; _js = js; _info = info; _text = text;
		_auth.AuthStateChanged += ResetAccountState;
	}

	public void ResetAccountState()
	{
		_generation++;
		Ready = false; Loading = false; Conflict = false; Error = null;
		Progress = new(); _anonymous = null; _key = null;
		Changed?.Invoke();
		if (_started && !_disposed) AuthorityRefresh = LoadAsync(_journey);
	}

	public async Task LoadAsync(GuideJourney journey)
	{
		await _auth.InitAsync();
		if (_disposed) return;
		_started = true;
		var generation = ++_generation;
		var token = Session;
		_journey = journey;
		Ready = false; Loading = true; Error = null; Conflict = false;
		Progress = new();
		Changed?.Invoke();
		try
		{
			var presentation = await _info.OnboardingAsync();
			if (!Current(generation, token)) return;
			Presentation = presentation;
			if (presentation is null || string.IsNullOrWhiteSpace(presentation.GameId))
				throw new InvalidOperationException(_text["GuideErrorUnavailable"].Value);
			_key = $"sharpmush.guide.v1:{presentation.GameId}:{journey}";
			var json = await _js.GetItemAsync(BrowserStore.Local, _key);
			if (!Current(generation, token)) return;
			_anonymous = ReadLocal(json, journey);
			OnboardingProgress progress;
			if (token is null) progress = _anonymous ?? new();
			else
			{
				using var request = Request(HttpMethod.Get, journey, token);
				using var client = _http.CreateClient("api");
				using var response = await client.SendAsync(request);
				response.EnsureSuccessStatusCode();
				progress = await response.Content.ReadFromJsonAsync<OnboardingProgress>() ?? throw new JsonException("Empty guide response.");
			}
			if (!Current(generation, token)) return;
			Progress = progress;
			if (!GuideStepIds.IsValid(journey, progress, progress.Revision))
				throw new InvalidOperationException(_text["GuideErrorVersion"].Value);
			Ready = true;
		}
		catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
		{
			if (Current(generation, token)) Error = error is InvalidOperationException ? error.Message : _text["GuideErrorLoad"].Value;
		}
		finally
		{
			if (Current(generation, token)) { Loading = false; Changed?.Invoke(); }
		}
	}

	public Task ChoosePathAsync(GuidePath path) => ChangeAsync(progress => progress with
	{
		Path = path,
		Active = path != GuidePath.Explore,
		Dismissed = path == GuidePath.Explore,
		CurrentStepId = StepsFor(path).Contains(progress.CurrentStepId ?? "") ? progress.CurrentStepId : StepsFor(path)[0]
	});
	public Task DismissAsync() => ChangeAsync(progress => progress with { Active = false, Dismissed = true });
	public Task SetImageryAsync(GuideImagery imagery) => ChangeAsync(progress => progress with { Imagery = imagery });
	public Task SetCurrentStepAsync(string step) => ChangeAsync(progress => progress with { CurrentStepId = step });
	public Task CompleteStepAsync(string step) => FinishStepAsync(step, false);
	public Task SkipStepAsync(string step) => FinishStepAsync(step, true);
	private IReadOnlyList<string> StepsFor(GuidePath path) => _journey == GuideJourney.Player
		? GuideCatalog.Player(path).Select(lesson => lesson.Id).ToArray() : GuideStepIds.Administrator;

	private Task FinishStepAsync(string step, bool skip) => ChangeAsync(progress =>
	{
		var completed = skip ? progress.CompletedStepIds.Except([step]).ToArray() : progress.CompletedStepIds.Union([step]).ToArray();
		var skipped = skip ? progress.SkippedStepIds.Union([step]).ToArray() : progress.SkippedStepIds.Except([step]).ToArray();
		var next = StepsFor(progress.Path).FirstOrDefault(id => !completed.Contains(id) && !skipped.Contains(id));
		return progress with { CompletedStepIds = completed, SkippedStepIds = skipped, CurrentStepId = next ?? step, Active = next is not null };
	});

	public async Task AcceptAnonymousContinuationAsync()
	{
		if (!HasAnonymousContinuation || _anonymous is not { } anonymous) return;
		await ChangeAsync(progress =>
		{
			var completed = progress.CompletedStepIds.Union(anonymous.CompletedStepIds).ToArray();
			var merged = progress.Revision == 0 ? anonymous with { Revision = 0 } : progress;
			return merged with
			{
				CompletedStepIds = completed,
				SkippedStepIds = progress.SkippedStepIds.Union(anonymous.SkippedStepIds).Except(completed).ToArray(),
				AnonymousContinuationHandled = true
			};
		});
	}
	public Task DeclineAnonymousContinuationAsync() => ChangeAsync(progress => progress with { AnonymousContinuationHandled = true });

	private async Task ChangeAsync(Func<OnboardingProgress, OnboardingProgress> change)
	{
		var generation = _generation;
		var token = Session;
		await _writes.WaitAsync();
		try
		{
			if (!Current(generation, token) || !Ready || Conflict) return;
			var revision = Progress.Revision;
			var next = change(Progress);
			if (!GuideStepIds.IsValid(_journey, next, revision)) throw new InvalidOperationException(_text["GuideErrorChoice"].Value);
			if (token is null)
			{
				next = next with { Revision = revision + 1 };
				var stored = await _js.SetItemAsync(BrowserStore.Local, _key!, JsonSerializer.Serialize(next));
				if (!Current(generation, token)) return;
				Progress = next;
				Error = stored ? null : _text["GuideErrorBrowserSave"].Value;
			}
			else
			{
				using var request = Request(HttpMethod.Put, _journey, token);
				request.Content = JsonContent.Create(new SaveOnboardingRequest(next, revision));
				using var client = _http.CreateClient("api");
				using var response = await client.SendAsync(request);
				if (!Current(generation, token)) return;
				if (response.StatusCode == HttpStatusCode.Conflict)
				{
					Conflict = true;
					Error = _text["GuideErrorConflict"].Value;
					return;
				}
				response.EnsureSuccessStatusCode();
				var saved = await response.Content.ReadFromJsonAsync<OnboardingProgress>();
				if (!Current(generation, token)) return;
				if (saved is null || saved.Revision != revision + 1 || !GuideStepIds.IsValid(_journey, saved, saved.Revision)) throw new JsonException("Invalid saved guide response.");
				Progress = saved;
				Error = null;
			}
		}
		catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
		{
			if (Current(generation, token)) Error = _text["GuideErrorSave"].Value;
		}
		finally { _writes.Release(); if (Current(generation, token)) Changed?.Invoke(); }
	}

	private bool Current(long generation, string? token) => !_disposed && generation == _generation && token == Session;
	private static HttpRequestMessage Request(HttpMethod method, GuideJourney journey, string token)
	{
		var request = new HttpRequestMessage(method, "api/onboarding/progress/" + (journey == GuideJourney.Player ? "player" : "administrator"));
		// Captured explicitly: the normal bearer handler must not retarget a queued save to a new account.
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return request;
	}
	private static OnboardingProgress? ReadLocal(string? json, GuideJourney journey)
	{
		if (string.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024) return null;
		try { return JsonSerializer.Deserialize<OnboardingProgress>(json); }
		catch (JsonException) { return new() { Dismissed = true }; }
	}
	public void Dispose()
	{
		_disposed = true; _generation++;
		_auth.AuthStateChanged -= ResetAccountState;
	}
}
