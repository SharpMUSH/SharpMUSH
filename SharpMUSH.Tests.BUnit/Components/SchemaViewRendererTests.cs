using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Verifies the read-only schema renderer shows field values from the data payload and honors the
/// softcode-owned per-viewer <c>visible</c> flag (a hidden datum does not render) — the same policy
/// the character profile uses.
/// </summary>
public class SchemaViewRendererTests : BunitContext
{
	public SchemaViewRendererTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

	private static PortalSchemaDocument ViewDoc() => new(
		Kind: "view",
		SchemaVersion: 1,
		Title: "Profile",
		DataSource: null,
		Pages:
		[
			new SchemaPage("p1", null, 1,
			[
				new SchemaSection("Demographics", 1, null,
				[
					new SchemaElement(Kind: "field", Key: "fullname", Label: "Full Name", Type: "text"),
					new SchemaElement(Kind: "field", Key: "secret", Label: "Secret", Type: "text")
				])
			], null, null)
		],
		Actions: null);

	[TUnit.Core.Test]
	public async Task RendersVisibleValues_AndHidesInvisibleOnes()
	{
		var data = new SchemaData(new Dictionary<string, SchemaFieldValue>
		{
			["fullname"] = new(Str("Gandalf the Grey"), Visible: true),
			["secret"] = new(Str("Olorin"), Visible: false)
		});

		var cut = Render<SchemaViewRenderer>(parameters => parameters
			.Add(p => p.Document, ViewDoc())
			.Add(p => p.Data, data));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Gandalf the Grey");
		await Assert.That(markup).Contains("Full Name");
		// The invisible field's value and (since it is the only datum on that field) its label must not show.
		await Assert.That(markup).DoesNotContain("Olorin");
	}
}
