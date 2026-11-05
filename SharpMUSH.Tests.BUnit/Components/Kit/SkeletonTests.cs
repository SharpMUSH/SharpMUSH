using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Pages;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// Loading placeholders hold the place of what is coming, so it arrives without moving the page. The one the
/// router shows during the sign-in check also opens the section sidebar's column, which the shell shows only
/// while something is mounted in it: the page moved across by the sidebar's width when the real one arrived.
/// </summary>
public class SkeletonTests : TrackingBunitContext
{
	public SkeletonTests() => Services.AddLocalization();

	[Test]
	[Arguments(typeof(SharpMUSH.Client.Pages.Help), true)]
	[Arguments(typeof(Home), false)]
	[Arguments(typeof(Login), false)]
	public async Task InSection_IsTheSectionLayoutPages(Type page, bool expected)
		=> await Assert.That(RouteSkeleton.InSection(page)).IsEqualTo(expected);

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task RouteSkeleton_FillsTheSidebarSlot_OnlyForASection(bool section)
	{
		RenderFragment body = b =>
		{
			b.OpenComponent<RouteSkeleton>(0);
			b.AddAttribute(1, nameof(RouteSkeleton.Section), section);
			b.CloseComponent();
		};

		var cut = Render<PageSidebarHost>(p => p.Add(x => x.ChildContent, body));

		await Assert.That(cut.FindAll("aside.test-pagebar .kit-pagebar").Count).IsEqualTo(section ? 1 : 0);
		await Assert.That(cut.FindAll(".kit-route-skeleton .kit-skeleton").Count).IsGreaterThan(0);
	}

	/// <summary>
	/// The shapes are hidden from a screen reader, which reads "Loading..." in their place. That text is not a live
	/// region: one inserted already holding its text is seldom announced, and a page loading several sections at
	/// once would have had several.
	/// </summary>
	[Test]
	public async Task LoadState_HoldsRowsWhileLoading_AndSaysSoToAScreenReader()
	{
		var cut = Render<LoadState<string>>(p => p
			.Add(x => x.Loading, true)
			.Add(x => x.LoadingRows, 3)
			.Add(x => x.ChildContent, value => b => b.AddContent(0, value)));

		await Assert.That(cut.FindAll(".kit-skeleton-row").Count).IsEqualTo(3);
		await Assert.That(cut.Find(".kit-skeleton-rows .visually-hidden").TextContent.Trim()).IsNotEmpty();
		await Assert.That(cut.FindAll("[role=status]").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-skeleton-row[aria-hidden=true]").Count).IsEqualTo(3);
	}
}
