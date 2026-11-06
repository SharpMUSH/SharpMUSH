using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components.Mail;

namespace SharpMUSH.Tests.BUnit.Pages;

public class MailDraftTests : TrackingBunitContext
{
	private readonly DraftStorage _storage = new();
	private readonly MailApiFake _api;

	public MailDraftTests()
	{
		_api = MailApiFake.Install(this);
		_api.Auth.Username.Returns("reviewer");
		Services.AddSingleton<IJSRuntime>(_storage);
	}

	[Test]
	public async Task RemountRestoresTypedDraft_AndDiscardClearsIt()
	{
		var cut = RenderComposer();
		await Assert.That(cut.Find(".mc-discard").HasAttribute("disabled")).IsTrue();
		await cut.InvokeAsync(() => cut.Find("#mc-to").InputAsync(new() { Value = "Wren" }));
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "An unsent invitation" }));
		cut.Dispose();
		var restored = RenderComposer();
		await Assert.That(restored.Find("#mc-to").GetAttribute("value")).IsEqualTo("Wren");
		await Assert.That(restored.Find("#mc-body").TextContent).Contains("An unsent invitation");
		await Assert.That(restored.Find(".mc-discard").HasAttribute("disabled")).IsFalse();
		await restored.Find(".mc-discard").ClickAsync();
		await Assert.That(restored.Find(".mc-discard").HasAttribute("disabled")).IsTrue();
		await Assert.That(_storage.Items.Count).IsEqualTo(0);
		restored.Dispose();
		await Assert.That(RenderComposer().Find("#mc-body").TextContent).IsEmpty();
	}

	[Test]
	public async Task ActorSwitchDoesNotCarryDraftIntoAnotherMailbox()
	{
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Ilsa private draft" }));
		await cut.InvokeAsync(_api.SwitchCharacter);
		cut.WaitForState(() => cut.Find("#mc-body").TextContent.Length == 0 && !cut.Find("#mc-body").HasAttribute("disabled"));
		await Assert.That(cut.Find("#mc-body").TextContent).IsEmpty();
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Wren private draft" }));
		await cut.InvokeAsync(() =>
		{
			_api.Auth.ActiveCharacter.Returns(MailApiFake.First);
			_api.Auth.ActiveCharacterChanged += Raise.Event<Action>();
		});
		cut.WaitForState(() => cut.Find("#mc-body").TextContent.Contains("Ilsa private draft", StringComparison.Ordinal) && !cut.Find("#mc-body").HasAttribute("disabled"));
		await Assert.That(cut.Find("#mc-body").TextContent).Contains("Ilsa private draft");
		await Assert.That(_storage.Items.Count).IsEqualTo(2);
	}

	[Test]
	public async Task BlockedStorageKeepsEditorUsable_AndExplainsDraftIsNotSaved()
	{
		_storage.BlockWrites = true;
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Keep these words" }));
		await Assert.That(cut.Find(".mc-draft-status").TextContent).Contains("MailDraftUnavailable");
		await Assert.That(cut.Find("#mc-body").TextContent).Contains("Keep these words");
		await Assert.That(cut.Find("#mc-body").HasAttribute("disabled")).IsFalse();
	}

	[Test]
	public async Task SuccessfulSendRemovesSavedDraft()
	{
		var client = Track(new HttpClient(new SendHandler()) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(new MailService(factory, _api.Auth));
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-to").InputAsync(new() { Value = "Wren" }));
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Send this" }));
		await cut.InvokeAsync(() => cut.FindAll(".mc-actions button").Last().ClickAsync());
		await Assert.That(_storage.Items.Count).IsEqualTo(0);
	}

	[Test]
	public async Task SuccessfulSendWithBlockedCleanupWarnsWithoutLeavingSentTextInEditor()
	{
		var client = Track(new HttpClient(new SendHandler()) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(new MailService(factory, _api.Auth));
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-to").InputAsync(new() { Value = "Wren" }));
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Already sent" }));
		_storage.BlockWrites = true;
		await cut.InvokeAsync(() => cut.FindAll(".mc-actions button").Last().ClickAsync());
		cut.WaitForState(() => cut.Find("#mc-body").TextContent.Length == 0 && !cut.Find("#mc-body").HasAttribute("disabled"));
		await Assert.That(cut.Find("#mc-body").TextContent).IsEmpty();
		await Assert.That(cut.Markup).Contains("MailDraftClearFailed");
		await Assert.That(_storage.Items.Count).IsEqualTo(1);
		_storage.BlockWrites = false;
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "A new draft" }));
		await Assert.That(cut.Markup).DoesNotContain("MailDraftClearFailed");
		await Assert.That(cut.Find(".mc-draft-status").TextContent).Contains("MailDraftSaved");
	}

	[Test]
	public async Task SuccessfulSendWithBlockedCleanupReadWarnsAboutRemainingDraft()
	{
		var client = Track(new HttpClient(new SendHandler()) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(new MailService(factory, _api.Auth));
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-to").InputAsync(new() { Value = "Wren" }));
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Already sent but unreadable saved copy" }));
		_storage.BlockReads = true;
		await cut.InvokeAsync(() => cut.FindAll(".mc-actions button").Last().ClickAsync());
		cut.WaitForState(() => cut.Find("#mc-body").TextContent.Length == 0 && !cut.Find("#mc-body").HasAttribute("disabled"));
		await Assert.That(cut.Find("#mc-body").TextContent).IsEmpty();
		await Assert.That(cut.Markup).Contains("MailDraftClearFailed");
		await Assert.That(_storage.Items.Count).IsEqualTo(1);
	}

	[Test]
	public async Task RefusedSendKeepsSavedDraft()
	{
		// MailApiFake has no send endpoint, so the actual service reports its 404 refusal.
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-to").InputAsync(new() { Value = "Missing" }));
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Keep after refusal" }));
		await cut.InvokeAsync(() => cut.FindAll(".mc-actions button").Last().ClickAsync());
		await Assert.That(_storage.Items.Count).IsEqualTo(1);
		cut.Dispose();
		await Assert.That(RenderComposer().Find("#mc-body").TextContent).Contains("Keep after refusal");
	}

	[Test]
	public async Task AnotherAccountDoesNotRestoreTheSameCharactersDraft()
	{
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Original account" }));
		await cut.InvokeAsync(() =>
		{
			_api.Auth.Username.Returns("another-reviewer");
			_api.Auth.AuthStateChanged += Raise.Event<Action>();
		});
		cut.WaitForState(() => cut.Find("#mc-body").TextContent.Length == 0 && !cut.Find("#mc-body").HasAttribute("disabled"));
		await Assert.That(cut.Find("#mc-body").TextContent).IsEmpty();
	}

	[Test]
	public async Task CompletingOldSendPreservesNewerDraftAfterRemount()
	{
		var handler = new DelayedSendHandler();
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(new MailService(factory, _api.Auth));
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-to").InputAsync(new() { Value = "Wren" }));
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "First message" }));
		var sending = cut.InvokeAsync(() => cut.FindAll(".mc-actions button").Last().ClickAsync());
		await handler.Started.Task;
		cut.Dispose();
		var newer = RenderComposer();
		await newer.InvokeAsync(() => newer.Find("#mc-body").InputAsync(new() { Value = "A new message" }));
		handler.Complete.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
		await sending;
		newer.Dispose();
		await Assert.That(RenderComposer().Find("#mc-body").TextContent).Contains("A new message");
	}

	[Test]
	public async Task LogoutClearsVisiblePrivateDraft()
	{
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Private words" }));
		await cut.InvokeAsync(() =>
		{
			_api.Auth.Username.Returns((string?)null);
			_api.Auth.AuthStateChanged += Raise.Event<Action>();
		});
		cut.WaitForState(() => cut.Find("#mc-body").TextContent.Length == 0 && !cut.Find("#mc-body").HasAttribute("disabled"));
		await Assert.That(cut.Find("#mc-body").TextContent).IsEmpty();
	}

	[Test]
	public async Task FailedDiscardPreservesVisibleDraft()
	{
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Find("#mc-body").InputAsync(new() { Value = "Keep until discard succeeds" }));
		_storage.BlockWrites = true;
		await cut.Find(".mc-discard").ClickAsync();
		await Assert.That(cut.Find("#mc-body").TextContent).Contains("Keep until discard succeeds");
		await Assert.That(cut.Find(".mc-draft-status").TextContent).Contains("MailDraftUnavailable");
	}

	private IRenderedComponent<MailCompose> RenderComposer()
	{
		var cut = Render<MailCompose>();
		cut.WaitForState(() => !cut.Find("#mc-body").HasAttribute("disabled"));
		return cut;
	}

	private sealed class DelayedSendHandler : HttpMessageHandler
	{
		public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource<HttpResponseMessage> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Started.SetResult();
			return Complete.Task;
		}
	}

	private sealed class SendHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
	}

	private sealed class DraftStorage : IJSRuntime
	{
		public Dictionary<string, string> Items { get; } = [];
		public bool BlockWrites { get; set; }
		public bool BlockReads { get; set; }
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		{
			if (identifier == "sessionStorage.getItem")
			{
				if (BlockReads) throw new JSException("Storage unavailable");
				return ValueTask.FromResult((TValue)(object?)Items.GetValueOrDefault((string)args![0]!)!);
			}
			if (identifier is "sessionStorage.setItem" or "sessionStorage.removeItem")
			{
				if (BlockWrites) throw new JSException("Storage unavailable");
				var key = (string)args![0]!;
				if (identifier.EndsWith("setItem", StringComparison.Ordinal)) Items[key] = (string)args[1]!;
				else Items.Remove(key);
			}
			return ValueTask.FromResult(default(TValue)!);
		}
	}
}
