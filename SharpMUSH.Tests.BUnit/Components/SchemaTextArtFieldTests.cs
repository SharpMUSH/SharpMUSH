using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>A <c>textart</c> schema field is a text box for text art: fixed-width, its lines unwrapped.</summary>
public class SchemaTextArtFieldTests : TrackingBunitContext
{
	private const string Art = " /\\_/\\\n( o.o )\n > ^ <";

	public SchemaTextArtFieldTests()
	{
		Services.AddMudServices().AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<SchemaFieldInput> RenderField(object? value, Action<object?>? changed = null) =>
		Render<SchemaFieldInput>(p => p
			.Add(x => x.Element, new SchemaElement(Kind: "field", Key: "art", Label: "Art", Type: "textart", Help: "Up to 16 lines."))
			.Add(x => x.Value, value)
			.Add(x => x.ValueChanged, v => changed?.Invoke(v)));

	[TUnit.Core.Test]
	public async Task ATextArtField_KeepsItsLinesUnwrapped()
	{
		var cut = RenderField(Art);

		var box = cut.Find(".schema-textart textarea");
		await Assert.That(box.GetAttribute("wrap")).IsEqualTo("off");
		await Assert.That(box.GetAttribute("spellcheck")).IsEqualTo("false");
		await Assert.That(cut.Markup).Contains("Up to 16 lines.");
	}

	[TUnit.Core.Test]
	public async Task TypingArt_ChangesTheValueAsTyped()
	{
		object? seen = null;
		var cut = RenderField(null, v => seen = v);

		await cut.Find(".schema-textart textarea").ChangeAsync(new() { Value = Art });

		await Assert.That(seen as string).IsEqualTo(Art);
	}
}
