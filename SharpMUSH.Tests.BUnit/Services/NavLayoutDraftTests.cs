using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services.Navigation;
using SharpMUSH.Library.Models.Portal.Navigation;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary><see cref="NavLayoutDraft"/>: the navigation editor's working copy and the steps staff take on it.</summary>
public class NavLayoutDraftTests
{
	private static PortalApplication App(string slug, string nav, int order = 0) =>
		new(slug, $"App {slug}", null, "Page", $"http/{slug}/schema", null, null, "Guest", nav, [], order);

	private static IReadOnlyList<NavDestination> Catalog(params PortalApplication[] apps) => NavCatalog.All(apps, key => key);

	private static string Keys(NavLayoutDraft draft, string group) =>
		string.Join(",", draft.Groups.Single(g => g.Id == group).Items.Select(i => i.Key));

	[Test]
	public async Task ANewGame_StartsFromTheDefault_WithNothingMarkedNew()
	{
		var draft = NavLayoutDraft.From(null, Catalog(App("boards", "Community")));

		await Assert.That(string.Join(",", draft.Groups.Select(g => g.Id))).IsEqualTo("play,world,Community");
		await Assert.That(draft.Arrivals).IsEmpty();
		await Assert.That(draft.Version).IsEqualTo(0);
	}

	[Test]
	public async Task WhatNoSavedLayoutPlaced_IsMarkedNew_WhereTheGamePutIt()
	{
		var stored = new NavLayout(NavLayout.CurrentFormat, 3,
			[new("play", null, NavGroupModes.Inline, null, [new("core:play"), new("core:scenes"), new("core:mail")])], [], []);

		var draft = NavLayoutDraft.From(stored, Catalog(App("boards", "Community")));

		await Assert.That(draft.Arrivals).Contains("app:boards");
		await Assert.That(draft.Arrivals).Contains("core:wiki");
		await Assert.That(draft.Arrivals).DoesNotContain("core:play");
		await Assert.That(draft.Where("app:boards")).IsEqualTo("Community");
		await Assert.That(draft.Version).IsEqualTo(3);
	}

	[Test]
	public async Task MovingAndNudging_KeepEachKeyInOnePlace()
	{
		var draft = NavLayoutDraft.From(null, Catalog());

		draft.Move("core:help", "play", 0);
		draft.Nudge("core:help", 1);
		draft.Move("core:map", NavLayoutDraft.HiddenId);
		draft.MoveBefore("core:wiki", "core:play");

		await Assert.That(Keys(draft, "play")).IsEqualTo("core:wiki,core:play,core:help,core:scenes,core:mail");
		await Assert.That(Keys(draft, "world")).IsEqualTo("core:characters");
		await Assert.That(string.Join(",", draft.Hidden)).IsEqualTo("core:map");
		await Assert.That(NavLayoutRules.Problem(draft.ToLayout())).IsNull();
	}

	[Test]
	public async Task ANewGroup_GetsAnIdFromItsName_AndARemovedGroupsItemsStay()
	{
		var draft = NavLayoutDraft.From(null, Catalog());

		var community = draft.AddGroup("Community & Events");
		var again = draft.AddGroup("Community & Events");
		draft.Move("core:wiki", community);
		draft.RemoveGroup(community);

		await Assert.That(community).IsEqualTo("community---events");
		await Assert.That(again).IsEqualTo("community---events-2");
		await Assert.That(draft.Where("core:wiki")).IsEqualTo("play");
	}

	[Test]
	public async Task ALink_IsAdded_CannotBeHidden_AndCanBeRemoved()
	{
		var draft = NavLayoutDraft.From(null, Catalog());

		var key = draft.AddLink("world", "Our Discord", "https://discord.gg/example", "chat", null);
		draft.Move(key, NavLayoutDraft.HiddenId);

		await Assert.That(key).IsEqualTo("link:our-discord");
		await Assert.That(draft.Where(key)).IsEqualTo("world");
		await Assert.That(NavLayoutRules.Problem(draft.ToLayout())).IsNull();

		draft.Remove(key);
		await Assert.That(draft.Where(key)).IsNull();
	}

	[Test]
	public async Task TheTabs_SaveAtMostThree_SkippingEmptySlots()
	{
		var draft = NavLayoutDraft.From(null, Catalog());
		draft.Tabs.Clear();
		draft.Tabs.AddRange(["core:play", "", "core:help", "core:wiki", "core:mail"]);

		await Assert.That(string.Join(",", draft.ToLayout().MobileTabs)).IsEqualTo("core:play,core:help,core:wiki");
	}
}
