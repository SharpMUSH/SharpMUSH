using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The list editor adds a value on Enter, on +, on losing focus and at a separator, never twice, and removes one by
/// its ×.
/// </summary>
public class ListEditorTests : TrackingBunitContext
{
	private IReadOnlyList<string> _items = [];

	public ListEditorTests() => Services.AddLocalization();

	private IRenderedComponent<ListEditor> Render(char[]? separators = null, Func<string, string>? normalize = null) =>
		Render<ListEditor>(p => p
			.Add(e => e.Items, _items)
			.Add(e => e.ItemsChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, items => _items = items))
			.Add(e => e.Label, "Hosts")
			.Add(e => e.Separators, separators ?? [','])
			.Add(e => e.Normalize, normalize));

	private static ChangeEventArgs Typed(string text) => new() { Value = text };

	[Test]
	public async Task EnterAddsTheTypedValue()
	{
		var cut = Render();

		await cut.Find(".list-editor-input").InputAsync(Typed(" api.example.com "));
		await cut.Find(".list-editor-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

		await Assert.That(_items).IsEquivalentTo(new[] { "api.example.com" });
	}

	[Test]
	public async Task ASeparatorEndsAValueAndAPastedListAddsEach()
	{
		var cut = Render();

		await cut.Find(".list-editor-input").InputAsync(Typed("red, green,bl"));

		await Assert.That(_items).IsEquivalentTo(new[] { "red", "green" });
		await Assert.That(cut.Find(".list-editor-input").GetAttribute("value")).IsEqualTo("bl");
	}

	[Test]
	public async Task LosingFocusAddsWhatWasTyped()
	{
		var cut = Render();

		await cut.Find(".list-editor-input").InputAsync(Typed("only"));
		await cut.Find(".list-editor-input").ChangeAsync(new ChangeEventArgs());

		await Assert.That(_items).IsEquivalentTo(new[] { "only" });
	}

	[Test]
	public async Task NormalizedDuplicatesAreNotAdded()
	{
		_items = ["api.example.com"];
		var cut = Render(normalize: value => value.ToLowerInvariant());

		await cut.Find(".list-editor-input").InputAsync(Typed("API.Example.com,new.example.com,"));

		await Assert.That(_items).IsEquivalentTo(new[] { "api.example.com", "new.example.com" });
	}

	[Test]
	public async Task TheCrossRemovesItsValue()
	{
		_items = ["a", "b", "c"];
		var cut = Render();

		await cut.FindAll(".list-editor-x")[1].ClickAsync(new MouseEventArgs());

		await Assert.That(_items).IsEquivalentTo(new[] { "a", "c" });
	}

	[Test]
	public async Task ThePlusButtonAddsAndWaitsForText()
	{
		var cut = Render();
		await Assert.That(cut.Find(".list-editor-add").HasAttribute("disabled")).IsTrue();

		await cut.Find(".list-editor-input").InputAsync(Typed("x"));
		await cut.Find(".list-editor-add").ClickAsync(new MouseEventArgs());

		await Assert.That(_items).IsEquivalentTo(new[] { "x" });
	}
}
