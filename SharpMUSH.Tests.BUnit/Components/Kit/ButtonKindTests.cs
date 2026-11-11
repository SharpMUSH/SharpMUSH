using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// The portal has three button styles (<see cref="ButtonKind"/>): primary (filled, accent), secondary (outlined)
/// and the quiet danger button (text, danger colour). Every <c>MudButton</c> names one; a button whose style
/// changes takes it from <see cref="ButtonKinds"/>.
/// </summary>
public partial class ButtonKindTests : TrackingBunitContext
{
	public ButtonKindTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[GeneratedRegex(@"<MudButton\b((?:[^>""']|""[^""]*""|'[^']*')*)>")]
	private static partial Regex MudButtonTag();

	private static readonly HashSet<(string?, string?)> Styles =
	[
		("Variant.Filled", "Color.Primary"),
		("Variant.Outlined", null),
		("Variant.Text", "Color.Error"),
	];

	private static string? Attribute(string tag, string name) =>
		Regex.Match(tag, $@"\s{name}=""([^""]*)""") is { Success: true } m ? m.Groups[1].Value : null;

	[Test]
	public async Task EveryMudButton_IsOneOfTheThreeStyles()
	{
		var files = Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories).ToList();
		var offenders = (
			from file in files
			let source = File.ReadAllText(file)
			from Match tag in MudButtonTag().Matches(source)
			let variant = Attribute(tag.Value, "Variant")
			let color = Attribute(tag.Value, "Color")
			let kinded = variant?.StartsWith("@ButtonKinds.Variant(", StringComparison.Ordinal) == true
				&& color?.StartsWith("@ButtonKinds.Color(", StringComparison.Ordinal) == true
			where !kinded && !Styles.Contains((variant, color))
			select $"{Path.GetRelativePath(ClientSource.RazorRoot, file)}:{source[..tag.Index].Count(c => c == '\n') + 1} {variant ?? "(no Variant)"} {color}"
		).ToList();

		await Assert.That(files.Count).IsGreaterThan(100).Because("the client's components are copied beside the tests");
		await Assert.That(offenders).IsEmpty();
	}

	[Test]
	public async Task NoHandMadeButtonStyles()
	{
		var styled = Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor*", SearchOption.AllDirectories)
			.Where(f => File.ReadAllText(f).Contains("acct-btn", StringComparison.Ordinal))
			.Select(f => Path.GetRelativePath(ClientSource.RazorRoot, f))
			.ToList();
		await Assert.That(styled).IsEmpty();
	}

	[Test]
	[Arguments(ButtonKind.Primary, Variant.Filled, Color.Primary)]
	[Arguments(ButtonKind.Secondary, Variant.Outlined, Color.Default)]
	[Arguments(ButtonKind.DangerQuiet, Variant.Text, Color.Error)]
	public async Task EachKind_HasItsVariantAndColour(ButtonKind kind, Variant variant, Color color)
	{
		await Assert.That(ButtonKinds.Variant(kind)).IsEqualTo(variant);
		await Assert.That(ButtonKinds.Color(kind)).IsEqualTo(color);
	}

	[Test]
	[Arguments("filled", "error", ButtonKind.DangerQuiet)]
	[Arguments("text", "Error", ButtonKind.DangerQuiet)]
	[Arguments("filled", null, ButtonKind.Primary)]
	[Arguments(null, "primary", ButtonKind.Primary)]
	[Arguments("outlined", "warning", ButtonKind.Secondary)]
	[Arguments(null, null, ButtonKind.Secondary)]
	[Arguments("text", "secondary", ButtonKind.Secondary)]
	public async Task ASchemaButton_TakesTheNearestStyle(string? variant, string? color, ButtonKind expected)
	{
		await Assert.That(SchemaRendering.ParseButtonKind(variant, color)).IsEqualTo(expected);
	}

	[Test]
	public async Task RowMenu_OpensItsItems_AndAnItemClosesItBeforeActing()
	{
		var chosen = 0;
		var cut = Render<MudHarness>(p => p.AddChildContent<RowMenu>(m => m
			.Add(x => x.Label, "More for Scenes")
			.AddChildContent<RowMenuItem>(i => i
				.Add(x => x.Danger, true)
				.Add(x => x.OnClick, () => chosen++)
				.AddChildContent("Uninstall"))));
		var button = cut.Find(".kit-popmenu > button");
		await Assert.That(button.GetAttribute("aria-label")).IsEqualTo("More for Scenes");
		await Assert.That(button.GetAttribute("aria-haspopup")).IsEqualTo("menu");
		await Assert.That(cut.FindAll(".kit-rowmenu-item").Count).IsEqualTo(0);

		await button.ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".kit-rowmenu-item--danger"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("[role='menu']").GetAttribute("aria-label")).IsEqualTo("More for Scenes");

		await cut.Find(".kit-rowmenu-item").ClickAsync();
		await Assert.That(chosen).IsEqualTo(1);
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mud-popover-open").Count > 0) throw new InvalidOperationException("menu still open");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task PageHeader_PutsTheSearchBeforeTheActions()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<PageHeader>(h => h
			.Add(x => x.Title, "Characters")
			.Add(x => x.Search, b =>
			{
				b.OpenComponent<PageSearch>(0);
				b.AddAttribute(1, nameof(PageSearch.Placeholder), "Search characters");
				b.AddAttribute(2, nameof(PageSearch.Id), "character-search");
				b.CloseComponent();
			})
			.Add(x => x.Actions, b => b.AddMarkupContent(0, "<button>New</button>"))));

		var head = cut.Find(".kit-page-head");
		var parts = head.Children.Select(c => c.ClassName).ToList();
		await Assert.That(parts).IsEquivalentTo(new[] { "kit-page-text", "kit-page-search", "kit-page-actions" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.Find(".kit-page-search").GetAttribute("role")).IsEqualTo("search");
		await Assert.That(cut.Find("#character-search").GetAttribute("aria-label")).IsEqualTo("Search characters");
	}
}
