using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>A <c>computed</c> schema field shows the value softcode worked out, and the reader cannot change it.</summary>
public class SchemaComputedFieldTests : TrackingBunitContext
{
	public SchemaComputedFieldTests()
	{
		Services.AddMudServices().AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task AComputedField_ShowsItsValueReadOnly()
	{
		var cut = Render<SchemaFieldInput>(p => p
			.Add(x => x.Element, new SchemaElement(Kind: "field", Key: "total", Label: "Total", Type: "computed", Help: "Worked out from your stats."))
			.Add(x => x.Value, "42"));

		var box = cut.Find("input");
		await Assert.That(box.GetAttribute("value")).IsEqualTo("42");
		await Assert.That(box.HasAttribute("readonly")).IsTrue();
		await Assert.That(cut.Markup).Contains("Worked out from your stats.");
	}
}
