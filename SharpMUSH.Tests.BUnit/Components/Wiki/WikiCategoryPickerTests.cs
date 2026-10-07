using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// The category picker keeps a new category's name as typed (the server titles its category page with it),
/// and files under an existing category by its key, so a page is never put in "Places" twice.
/// </summary>
public class WikiCategoryPickerTests : TrackingBunitContext
{
	private static readonly WikiCategorySummaryDto[] Known =
	[
		new("places", "Places", 4, HasPage: true),
		new("lore", "Lore", 2, HasPage: false),
	];

	public WikiCategoryPickerTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private (IRenderedComponent<MudHarness> Host, List<string> Categories) RenderPicker(List<string> categories)
	{
		var host = Render<MudHarness>(p => p.AddChildContent<WikiCategoryPicker>(picker => picker
			.Add(c => c.Categories, categories)
			.Add(c => c.Known, Known)));
		return (host, categories);
	}

	[Test]
	public async Task A_new_category_is_offered_and_kept_as_typed()
	{
		var (host, categories) = RenderPicker([]);

		await host.Find(".wcp-entry input").InputAsync("Magic Items");
		await host.Find(".wcp-option--new").ClickAsync();

		await Assert.That(categories).IsEquivalentTo(new[] { "Magic Items" });
		await Assert.That(host.Find(".wcp-chip").TextContent).Contains("Magic Items");
	}

	[Test]
	public async Task Enter_on_an_existing_categorys_name_files_under_its_key_once()
	{
		var (host, categories) = RenderPicker(["places"]);

		var input = host.Find(".wcp-entry input");
		await input.InputAsync("PLACES");
		await Assert.That(host.FindAll(".wcp-option--new")).IsEmpty();
		await host.Find(".wcp-entry input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

		await Assert.That(categories).IsEquivalentTo(new[] { "places" });
	}

	[Test]
	public async Task Suggestions_leave_out_what_the_page_already_holds()
	{
		var (host, _) = RenderPicker(["places"]);

		var offered = host.FindAll(".wcp-option").Select(o => o.TextContent).ToList();

		await Assert.That(offered.Any(o => o.Contains("Lore"))).IsTrue();
		await Assert.That(offered.Any(o => o.Contains("Places"))).IsFalse();
	}
}
