using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// A trivial Blazor component standing in for a plugin-shipped compiled component. In bUnit it lives in the
/// already-loaded test assembly; rendering it via &lt;DynamicComponent Type="..."&gt; proves the resolve→render
/// seam the production loader hands off to. The in-browser Mono <c>Assembly.Load(byte[])</c> step that loads a
/// real plugin .wasm is runtime-only (not exercisable in bUnit's server-side test runtime) and is documented
/// as such — see <see cref="PluginComponentLoader"/>.
/// </summary>
public sealed class FakePluginComponent : ComponentBase
{
	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.OpenElement(0, "div");
		builder.AddAttribute(1, "data-testid", "plugin-component");
		builder.AddContent(2, "Hello from a plugin component");
		builder.CloseElement();
	}
}

/// <summary>Drives a non-success HTTP fetch so the loader's gate-off / verification-fail path returns null.</summary>
file sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		=> Task.FromResult(new HttpResponseMessage(status));
}

public class PluginComponentLoaderTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task DynamicComponent_RendersResolvedType()
	{
		// The render seam: given a resolved component Type (as the loader would hand off after Assembly.Load),
		// <DynamicComponent> renders it. This is exactly what DynamicApplication.razor does for Component apps.
		var cut = Render<DynamicComponent>(p => p.Add(x => x.Type, typeof(FakePluginComponent)));

		await Assert.That(cut.Markup).Contains("plugin-component");
		await Assert.That(cut.Markup).Contains("Hello from a plugin component");
	}

	[TUnit.Core.Test]
	public async Task ResolveComponentAsync_GateOffOr404_ReturnsNull()
	{
		// A 404 (the server endpoint when allow_browser_code is off, or an unverified assembly) must make the
		// loader a no-op — defense in depth on the client side.
		var http = new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri("http://localhost/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var loader = new PluginComponentLoader(factory, NullLogger<PluginComponentLoader>.Instance);
		var type = await loader.ResolveComponentAsync("api/plugins/p/ui/Comp.dll", "Comp.Widget");

		await Assert.That(type).IsNull().Because("a non-success fetch must not load or resolve a component");
	}

	[TUnit.Core.Test]
	public async Task ResolveComponentAsync_BlankInputs_ReturnsNull()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		var loader = new PluginComponentLoader(factory, NullLogger<PluginComponentLoader>.Instance);

		await Assert.That(await loader.ResolveComponentAsync("", "Comp.Widget")).IsNull();
		await Assert.That(await loader.ResolveComponentAsync("api/x.dll", "")).IsNull();
	}
}
