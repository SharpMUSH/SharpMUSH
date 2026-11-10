using System.Text.Json.Nodes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Builder;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

public class BuilderElementSettingsTests : TrackingBunitContext
{
	[Test]
	public async Task ArrowNavigationMovesFocusWithSelection_AndContinuesAcrossTabs()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		var element = JsonNode.Parse("""{"kind":"markdown","value":"Hello"}""")!.AsObject();
		var cut = Render<BuilderElementSettings>(p => p
			.Add(c => c.Element, element)
			.Add(c => c.Draft, new BuilderDraft())
			.Add(c => c.Columns, 2));

		await cut.Find("#bld-tab-content").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowRight" });
		await Assert.That(cut.Find("#bld-tab-layout").GetAttribute("aria-selected")).IsEqualTo("true");
		await Assert.That(JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus")).IsEqualTo(1);
		await cut.Find("#bld-tab-layout").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowRight" });
		await Assert.That(cut.Find("#bld-tab-content").GetAttribute("aria-selected")).IsEqualTo("true");
		await Assert.That(JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus")).IsEqualTo(2);
	}
}
