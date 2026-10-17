using System.Net;
using System.Globalization;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin.Packages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Packages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class PackageBatchReviewTests : BunitContext, IAsyncDisposable
{
	private readonly BatchHandler handler = new();
	private readonly HttpClient client;
	private readonly PackagesAdminService packages;
	private readonly AccountAuthService accountAuth;

	public PackageBatchReviewTests()
	{
		var auth = this.AddAuthorization();
		auth.SetAuthorized("headwiz");
		auth.SetPolicies(PortalPermission.PackagesAdmin);
		client = new(handler) { BaseAddress = new("https://localhost/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		accountAuth = new(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		packages = new(factory, new StubServerInfoService(guestsEnabled: true), accountAuth);
		Services.AddMudServices().AddSingleton(packages)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	public async Task Plugin_off_is_direct_only_without_dependents(bool hasDependents, bool conflict)
	{
		handler.BlockPluginOff = conflict;
		handler.Plugin = new("example", "Example", "1.0", null, PluginOrigin.BuiltIn,
			PluginBootStatus.Loaded, null, true, PluginPendingChange.None, true, null, 1, 1,
			hasDependents ? ["dependent"] : []);
		var host = Render<MudHarness>(p => p.AddChildContent<AdminPackages>());
		host.WaitForAssertion(() => host.FindAll("button.pkg-tab").Single(b => b.TextContent.Contains("PkgPlugins")));
		await host.FindAll("button.pkg-tab").Single(b => b.TextContent.Contains("PkgPlugins")).ClickAsync();
		await host.Find("button[aria-label='PkgPluginTurnOffNamed(Example)']").ClickAsync();
		await Assert.That(handler.PluginChanges).IsEqualTo(hasDependents || conflict ? 0 : 1);
		await Assert.That(packages.Batch.Intents.Count).IsEqualTo(hasDependents || conflict ? 1 : 0);
		await Assert.That(host.Markup).DoesNotContain("PkgPluginDisableTitle");
	}

	[Test]
	public async Task Saving_an_install_review_returns_choices_to_the_batch_without_single_apply()
	{
		var intent = new PackageBatchIntent("example", PackageBatchAction.Install, new("official", "example"), "row");
		packages.Batch.Queue(intent);
		var changes = new PackageChangeset("example", null, "1.0", PackageRevisionKind.Install, [], [], [], [], [], []);
		var step = new PackageBatchStep("install-step", intent, "Requested",
			Review: new("example", "1.0", "abc", changes, [], [], []));
		var plan = new PackageBatchPlanResponse([step], [], [], "token", DateTimeOffset.UtcNow.AddMinutes(5));
		packages.Batch.SetPlan(plan);
		handler.Plan = _ => plan;
		Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/packages/review?remote=official&path=example&batchStep=install-step");
		var host = Render<MudHarness>(p => p.AddChildContent<AdminPackageReview>());
		host.WaitForAssertion(() => host.FindAll("button").Single(b => b.TextContent.Trim() == "PkgBatchSaveChoices"));
		await Assert.That(packages.Batch.IsReviewed("install-step")).IsFalse();
		await host.FindAll("button").Single(b => b.TextContent.Trim() == "PkgBatchSaveChoices").ClickAsync();
		await Assert.That(packages.Batch.IsReviewed("install-step")).IsTrue();
		await Assert.That(handler.SingleApplies).IsEqualTo(0);
		await Assert.That(packages.Batch.ReviewFor("install-step")).IsNotNull();
		await Assert.That(Services.GetRequiredService<NavigationManager>().Uri).EndsWith("/admin/packages/batch");
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Only_install_steps_needing_choices_require_a_final_review(bool settingsChange)
	{
		var intent = new PackageBatchIntent("example", PackageBatchAction.Install, new("official", "example"), "row");
		packages.Batch.Queue(intent);
		var changes = new PackageChangeset("example", null, "1.0", PackageRevisionKind.Install, [], [], [], [], [], [],
			Settings: settingsChange ? [new("messages_object", PackageSettingAction.Set, null, "#12", "Creates object")] : []);
		handler.Plan = _ => new([new("install-step", intent, "Requested", Review: new("example", "1.0", "abc", changes, [], [], []))],
			[], [], "token", DateTimeOffset.UtcNow.AddMinutes(5));
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		host.WaitForAssertion(() => host.FindAll("button").Single(b => b.TextContent.Trim() == "PkgBatchApply"));
		await Assert.That(host.FindAll("button").Single(b => b.TextContent.Trim() == "PkgBatchApply").HasAttribute("disabled")).IsEqualTo(settingsChange);
	}

	[Test]
	public async Task Proposed_prerequisite_is_added_only_after_the_admin_selects_it()
	{
		packages.Batch.Queue(new("dependent", PackageBatchAction.Uninstall));
		handler.Plan = request => request.Intents.Count == 1
			? new([new("dependent-step", request.Intents[0], "Requested")],
				[new(new("prerequisite", PackageBatchAction.Uninstall), "Required first")],
				[new("dependent", "dependent", "Review dependent removal")], null, null)
			: new(request.Intents.Select(i => new PackageBatchStep(i.TargetId, i, "Accepted")).ToList(), [], [], "review", DateTimeOffset.UtcNow.AddMinutes(5));
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		host.WaitForAssertion(() => host.FindAll("button").Single(b => b.TextContent.Contains("PkgBatchAddChange")));
		await Assert.That(packages.Batch.Intents.Count).IsEqualTo(1);
		await host.FindAll("button").Single(b => b.TextContent.Contains("PkgBatchAddChange")).ClickAsync();
		await Assert.That(packages.Batch.Intents.Count).IsEqualTo(2);
		await Assert.That(handler.Plans.Last().Intents.Select(i => i.TargetId).ToList()).Contains("prerequisite");
	}

	[Test]
	public async Task Partial_results_preserve_completed_failed_and_unrun_steps_and_review_links()
	{
		packages.Batch.Run = new("run", PackageBatchRunState.Partial,
			[new("a", new("a", PackageBatchAction.Uninstall), PackageBatchStepState.Completed),
			new("b", new("b", PackageBatchAction.Install), PackageBatchStepState.Failed, "Conflict"),
			new("c", new("c", PackageBatchAction.Install), PackageBatchStepState.NeedsReview, ReviewUrl: "/admin/packages/review?remote=official&path=c"),
			new("d", new("d", PackageBatchAction.Install), PackageBatchStepState.Unrun)]);
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		await Assert.That(host.Markup).Contains("PkgBatchRunPartial");
		await Assert.That(host.Markup).Contains("PkgBatchStepCompleted");
		await Assert.That(host.Markup).Contains("PkgBatchStepFailed");
		await Assert.That(host.Markup).Contains("PkgBatchStepUnrun");
		await Assert.That(host.FindAll("button").Any(b => b.TextContent.Trim() == "PkgReview")).IsTrue();
		await Assert.That(host.Markup).DoesNotContain("PkgBatchRunCompleted");
	}

	[Test]
	[Arguments("remove")]
	[Arguments("edit")]
	[Arguments("reorder")]
	[Arguments("configure")]
	[Arguments("logout")]
	[Arguments("dispose")]
	public async Task A_late_plan_cannot_restore_a_token_after_draft_or_authority_changes(string change)
	{
		packages.Batch.Queue(new("a", PackageBatchAction.Uninstall));
		packages.Batch.SetPlan(new([new("a", packages.Batch.Intents[0], "Requested")], [], [], null, null));
		handler.WaitForPlan = new(TaskCreationOptions.RunContinuationsAsynchronously);
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		host.WaitForAssertion(() =>
		{
			if (handler.Plans.Count == 0) throw new InvalidOperationException("plan request not sent");
		});
		switch (change)
		{
			case "remove": packages.Batch.Remove(packages.Batch.Intents[0].ClientIntentId!); break;
			case "edit": packages.Batch.Queue(new("a", PackageBatchAction.Install, new("official", "a", "2.0"))); break;
			case "reorder":
				packages.Batch.Queue(new("b", PackageBatchAction.PluginOff));
				packages.Batch.Remove(packages.Batch.Intents[0].ClientIntentId!);
				packages.Batch.Queue(new("a", PackageBatchAction.Uninstall));
				break;
			case "configure": packages.Batch.SaveReview(new("a", ConfigureAnswers: new() { ["password"] = "private-value" })); break;
			case "logout": await accountAuth.LogoutAsync(); break;
			case "dispose": await host.FindComponent<PackageBatchReview>().Instance.DisposeAsync(); break;
		}
		handler.WaitForPlan.SetResult();
		host.WaitForAssertion(() =>
		{
			if (host.FindAll(".mud-progress-linear").Count != 0) throw new InvalidOperationException("plan still pending");
		});
		await Assert.That(packages.Batch.Plan?.ReviewToken).IsNull();
		await Assert.That(host.FindAll("button").Any(b => b.TextContent.Contains("PkgBatchApply") && !b.HasAttribute("disabled"))).IsFalse();
	}

	[Test]
	public async Task Lost_status_is_unknown_and_never_claims_completion()
	{
		packages.Batch.Run = new("lost", PackageBatchRunState.Running,
			[new("a", new("a", PackageBatchAction.Uninstall), PackageBatchStepState.Running)]);
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		host.WaitForAssertion(() =>
		{
			if (!host.Markup.Contains("PkgBatchUnknownOutcome")) throw new InvalidOperationException("unknown outcome not shown");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(host.Markup).Contains("PkgBatchRunUnknown");
		await Assert.That(host.Markup).DoesNotContain("PkgBatchRunCompleted");
	}

	[Test]
	public async Task An_accepted_run_updates_the_current_page_after_navigation()
	{
		packages.Batch.Queue(new("a", PackageBatchAction.PluginOff));
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		await host.InvokeAsync(() => packages.Batch.Run = new("accepted", PackageBatchRunState.Pending, []));
		host.WaitForAssertion(() =>
		{
			if (!host.Markup.Contains("PkgBatchRunPending")) throw new InvalidOperationException("accepted run not observed");
		});
		await Assert.That(host.FindAll("button").Any(b => b.TextContent.Contains("PkgBatchApply") && !b.HasAttribute("disabled"))).IsFalse();
	}

	[Test]
	public async Task Result_review_replans_remaining_changes_and_opens_the_new_batch_step()
	{
		packages.Batch.Queue(new("a", PackageBatchAction.Uninstall));
		packages.Batch.Queue(new("b", PackageBatchAction.Install, new("official", "b")));
		var failed = packages.Batch.Intents[1];
		packages.Batch.Run = new("run", PackageBatchRunState.Partial,
			[new("done", packages.Batch.Intents[0], PackageBatchStepState.Completed),
			new("old-step", failed, PackageBatchStepState.NeedsReview, ReviewUrl: "/admin/packages/review?remote=official&path=b")]);
		var changes = new PackageChangeset("b", null, "1.0", PackageRevisionKind.Install, [], [], [], [], [], []);
		handler.Plan = request => new([new(handler.Plans.Count == 1 ? "initial-step" : "fresh-step", request.Intents[0], "Requested",
			Review: new("b", "1.0", "abc", changes, [], [], []))], [], [], "token", DateTimeOffset.UtcNow.AddMinutes(5));
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		await host.FindAll("button").Single(b => b.TextContent.Trim() == "PkgReview").ClickAsync();
		await Assert.That(handler.Plans.Count).IsEqualTo(2);
		await Assert.That(handler.Plans.Last().Intents.Select(i => i.TargetId).ToList()).IsEquivalentTo(["b"]);
		await Assert.That(Services.GetRequiredService<NavigationManager>().Uri).Contains("batchStep=fresh-step");
		await Assert.That(handler.SingleApplies).IsEqualTo(0);
	}

	[Test]
	public async Task A_new_draft_does_not_display_a_cached_completed_run()
	{
		packages.Batch.Run = new("old-run", PackageBatchRunState.Completed, []);
		packages.Batch.Queue(new("new-package", PackageBatchAction.Uninstall));
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		await Assert.That(packages.Batch.Run).IsNull();
		await Assert.That(host.Markup).DoesNotContain("PkgBatchRunCompleted");
	}

	[Test]
	public async Task Disposing_progress_cancels_its_read_without_cancelling_the_accepted_run()
	{
		handler.WaitForStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
		packages.Batch.Run = new("run", PackageBatchRunState.Running, []);
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		await handler.StatusReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await host.FindComponent<PackageBatchReview>().Instance.DisposeAsync();
		await Assert.That(handler.StatusCancelled).IsTrue();
		await Assert.That(packages.Batch.Run?.State).IsEqualTo(PackageBatchRunState.Running);
	}

	[Test]
	public async Task A_late_progress_response_after_logout_does_not_restore_the_previous_run()
	{
		handler.WaitForStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
		packages.Batch.Run = new("run", PackageBatchRunState.Running, []);
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		await handler.StatusReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await accountAuth.LogoutAsync();
		handler.WaitForStatus.SetResult();
		await handler.StatusReadReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await host.FindComponent<PackageBatchReview>().Instance.DisposeAsync();
		await Assert.That(packages.Batch.Run).IsNull();
	}

	[Test]
	public async Task Retry_plans_actual_remaining_intent_without_reapplying_completed_steps()
	{
		packages.Batch.Queue(new("a", PackageBatchAction.Uninstall));
		packages.Batch.Queue(new("b", PackageBatchAction.PluginOff));
		packages.Batch.Run = new("run", PackageBatchRunState.AwaitingRestart,
			[new("a", packages.Batch.Intents[0], PackageBatchStepState.Completed),
			new("b", packages.Batch.Intents[1], PackageBatchStepState.AwaitingRestart)]);
		var host = Render<MudHarness>(p => p.AddChildContent<PackageBatchReview>());
		await host.FindAll("button").Single(b => b.TextContent.Contains("PkgBatchReviewRemaining")).ClickAsync();
		await Assert.That(handler.Plans.Last().Intents.Select(i => i.TargetId).ToList()).IsEquivalentTo(["b"]);
		await Assert.That(handler.Applies).IsEqualTo(0);
	}

	[Test]
	public async Task Changed_affected_state_does_not_reuse_an_uninstall_confirmation()
	{
		packages.Batch.Queue(new("a", PackageBatchAction.Uninstall));
		packages.Batch.ConfirmUninstall("a", "1.0", 1);
		packages.Batch.SetPlan(new([new("old", packages.Batch.Intents[0], "Requested", "1.0", 1)], [], [], null, null));
		await Assert.That(packages.Batch.ReviewFor("old")?.ConfirmUninstall is true).IsTrue();
		packages.Batch.SetPlan(new([new("new", packages.Batch.Intents[0], "Changed", "2.0", 2)], [], [], null, null));
		await Assert.That(packages.Batch.ReviewFor("old")).IsNull();
		await Assert.That(packages.Batch.ReviewFor("new")).IsNull();
	}

	[Test]
	public async Task Batch_resources_resolve_and_format_under_every_declared_locale()
	{
		var neutral = SharedResources.Manager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
		var keys = neutral.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key)
			.Where(k => k.StartsWith("PkgBatch", StringComparison.Ordinal)).ToList();
		await Assert.That(keys.Count).IsGreaterThan(30);
		foreach (var locale in PortalLocales.Codes)
		{
			var culture = CultureInfo.GetCultureInfo(locale);
			foreach (var key in keys)
			{
				var value = SharedResources.Manager.GetString(key, culture);
				await Assert.That(string.IsNullOrWhiteSpace(value)).IsFalse();
				_ = string.Format(culture, value!, "example", 1);
			}
		}
	}

	public new async ValueTask DisposeAsync()
	{
		packages.Dispose();
		client.Dispose();
		await base.DisposeAsync();
	}

	private sealed class BatchHandler : HttpMessageHandler
	{
		public List<PackageBatchPlanRequest> Plans { get; } = [];
		public int Applies { get; private set; }
		public int SingleApplies { get; private set; }
		public int PluginChanges { get; private set; }
		public bool BlockPluginOff { get; set; }
		public TaskCompletionSource? WaitForPlan { get; set; }
		public TaskCompletionSource? WaitForStatus { get; set; }
		public TaskCompletionSource StatusReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource StatusReadReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public bool StatusCancelled { get; private set; }

		public PluginStatusDto? Plugin { get; set; }

		public Func<PackageBatchPlanRequest, PackageBatchPlanResponse> Plan { get; set; } = request =>
			new(request.Intents.Select(i => new PackageBatchStep(i.TargetId, i, "Requested")).ToList(), [], [], "review", DateTimeOffset.UtcNow.AddMinutes(5));

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path == "/api/packages")
				return new(HttpStatusCode.OK) { Content = JsonContent.Create<IReadOnlyList<InstalledPackageDto>>([]) };
			if (path == "/api/packages/plugins")
				return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PluginsResponse(Plugin is null ? [] : [Plugin], true, false)) };
			if (path == "/api/packages/plugins/example/disable" && Plugin is { } plugin)
			{
				if (BlockPluginOff) return new(HttpStatusCode.Conflict) { Content = new StringContent("Review dependent plugin changes in a batch") };
				PluginChanges++;
				return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PluginChangeResponse(plugin with { Enabled = false }, [], [])) };
			}
			if (path == "/api/packages/apply") SingleApplies++;
			if (request.RequestUri!.AbsolutePath == "/api/packages/batches/plan")
			{
				var body = (await request.Content!.ReadFromJsonAsync<PackageBatchPlanRequest>(cancellationToken))!;
				Plans.Add(body);
				if (WaitForPlan is { } wait) await wait.Task;
				return new(HttpStatusCode.OK) { Content = JsonContent.Create(Plan(body)) };
			}
			if (request.Method == HttpMethod.Get && path.StartsWith("/api/packages/batches/", StringComparison.Ordinal) && WaitForStatus is { } status)
			{
				StatusReadStarted.TrySetResult();
				try { await status.Task.WaitAsync(cancellationToken); }
				catch (OperationCanceledException) { StatusCancelled = true; throw; }
				StatusReadReturned.TrySetResult();
				return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PackageBatchRunResponse("run", PackageBatchRunState.Completed, [])) };
			}
			if (request.RequestUri.AbsolutePath == "/api/packages/batches/apply") Applies++;
			return new(HttpStatusCode.Gone) { Content = new StringContent("Progress expired") };
		}
	}
}
