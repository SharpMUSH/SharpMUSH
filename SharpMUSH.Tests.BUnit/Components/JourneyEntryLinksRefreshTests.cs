using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

internal sealed class RefreshableJourneyInfo() : ServerInfoService(null!)
{
	public Func<Task<OnboardingPresentation?>> Read { get; set; } = () => Task.FromResult<OnboardingPresentation?>(null);
	public int Reads { get; private set; }

	public override Task<OnboardingPresentation?> OnboardingAsync()
	{
		Reads++;
		return Read();
	}
}

/// <summary>Uses the real mounted component and inherited ServerInfoService.Refresh notification.</summary>
public class JourneyEntryLinksRefreshTests : BunitContext
{
	private static OnboardingPresentation Projection(string name) => new("game", "", "", "", "", "",
		new GuideLink("Welcome " + name, "/wiki/main/" + name), null, null);

	private IRenderedComponent<JourneyEntryLinks> Mount(RefreshableJourneyInfo info)
	{
		Services.AddEchoLocalizer();
		Services.AddSingleton<ServerInfoService>(info);
		return Render<JourneyEntryLinks>();
	}

	[Test]
	public async Task MountedLinksFollowRefreshReplacingAndRemovingConfiguredDestinations()
	{
		OnboardingPresentation? current = Projection("old");
		var info = new RefreshableJourneyInfo { Read = () => Task.FromResult<OnboardingPresentation?>(current) };
		var cut = Mount(info);
		await Assert.That(cut.Find("a[href='/wiki/main/old']").TextContent).IsEqualTo("Welcome old");

		current = new OnboardingPresentation("game", "", "", "", "", "", null,
			new GuideLink("Replacement rules", "/wiki/policy/new_rules"), new GuideLink("New guide", "/wiki/main/new_guide"));
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => cut.Find("a[href='/wiki/policy/new_rules']"));
		await Assert.That(cut.FindAll("a[href='/wiki/main/old']")).IsEmpty();
		await Assert.That(cut.Find("a[href='/wiki/policy/new_rules']").TextContent).IsEqualTo("Replacement rules");
		await Assert.That(cut.Find("a[href='/wiki/main/new_guide']").TextContent).IsEqualTo("New guide");

		current = new OnboardingPresentation("game", "", "", "", "", "", null, null, null);
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => { if (cut.FindAll(".journey-about").Count != 0) throw new InvalidOperationException("removed links remain"); });
		await Assert.That(cut.FindAll("a").Select(a => a.GetAttribute("href") ?? "").ToArray())
			.IsEquivalentTo(new[] { "/welcome", "/help" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task FailedProjectionCanRecoverAndClearAgainOnRefresh()
	{
		// OnboardingAsync returns null on the existing service's failed/unavailable public projection.
		OnboardingPresentation? current = null;
		var info = new RefreshableJourneyInfo { Read = () => Task.FromResult<OnboardingPresentation?>(current) };
		var cut = Mount(info);
		await Assert.That(cut.FindAll(".journey-about")).IsEmpty();
		current = Projection("recovered");
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => cut.Find("a[href='/wiki/main/recovered']"));
		current = null;
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => { if (cut.FindAll(".journey-about").Count != 0) throw new InvalidOperationException("failed projection kept links"); });
		current = Projection("later");
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => cut.Find("a[href='/wiki/main/later']"));
		await Assert.That(cut.FindAll("a[href='/wiki/main/recovered']")).IsEmpty();
	}

	[Test]
	public async Task OlderOverlappingResponseCannotRestoreRemovedLinks()
	{
		var info = new RefreshableJourneyInfo { Read = () => Task.FromResult<OnboardingPresentation?>(Projection("initial")) };
		var cut = Mount(info);
		var older = new TaskCompletionSource<OnboardingPresentation?>();
		var newer = new TaskCompletionSource<OnboardingPresentation?>();
		info.Read = () => older.Task;
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => { if (info.Reads != 2) throw new InvalidOperationException("first refresh did not start"); });
		await Assert.That(cut.FindAll(".journey-about")).IsEmpty().Because("old links are withdrawn while their projection is re-read");
		info.Read = () => newer.Task;
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => { if (info.Reads != 3) throw new InvalidOperationException("newest refresh did not start"); });
		// Default TCS + renderer-dispatcher completion makes the controlled continuations deterministic.
		await cut.InvokeAsync(() => newer.SetResult(new OnboardingPresentation("game", "", "", "", "", "", null, null, null)));
		await cut.InvokeAsync(() => older.SetResult(Projection("stale")));
		await Assert.That(cut.FindAll(".journey-about")).IsEmpty();
		await Assert.That(cut.FindAll("a[href='/wiki/main/stale']")).IsEmpty();
	}

	[Test]
	public async Task InitialPendingResponseCannotOverwriteARefreshedTitleAndRoute()
	{
		var initial = new TaskCompletionSource<OnboardingPresentation?>();
		var info = new RefreshableJourneyInfo { Read = () => initial.Task };
		var cut = Mount(info);
		info.Read = () => Task.FromResult<OnboardingPresentation?>(Projection("new"));
		await cut.InvokeAsync(info.Refresh);
		cut.WaitForAssertion(() => cut.Find("a[href='/wiki/main/new']"));
		await cut.InvokeAsync(() => initial.SetResult(Projection("old")));
		await Assert.That(cut.FindAll("a[href='/wiki/main/old']")).IsEmpty();
		await Assert.That(cut.Find("a[href='/wiki/main/new']").TextContent).IsEqualTo("Welcome new");
	}

	[Test]
	public async Task DisposalUnsubscribesFromFurtherRefreshReads()
	{
		var info = new RefreshableJourneyInfo { Read = () => Task.FromResult<OnboardingPresentation?>(Projection("initial")) };
		Mount(info);
		var reads = info.Reads;
		await DisposeComponentsAsync();
		info.Refresh();
		await Assert.That(info.Reads).IsEqualTo(reads);
	}
}
