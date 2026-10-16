using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

public class SchemaApplicationHostTests : BunitContext
{
	private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
	}

	private AccountAuthService Configure(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(new HttpClient(new Handler(send)) { BaseAddress = new("https://localhost/") });
		Services.AddMudServices().AddSingleton(factory)
			.AddSingleton(sp => new SchemaAppService(factory, NullLogger<SchemaAppService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		Services.AddSingleton(auth);
		return auth;
	}

	private static PortalSchemaDocument Screen(string kind, string title) => new(kind, 1, title, null,
		[new("main", null, 1, [new(null, 1, null,
			[new(Kind: "field", Key: "title", Default: JsonSerializer.SerializeToElement("draft")),
			 new(Kind: "button", Label: "Go", Action: "submit")])], null, null)],
		new Dictionary<string, SchemaAction> { ["submit"] = new("http", "POST", "/http/test", "fields", null, null) });
	private static HttpResponseMessage Reply(SchemaActionResult result) => new(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: SchemaJson.Options) };

	[Test]
	public async Task HostsAndReplacementDocumentsUseDistinctFormInstances()
	{
		var instances = new List<string>();
		Configure(request =>
		{
			instances.Add(request.Headers.GetValues("X-SharpMUSH-Form-Instance").Single());
			return Task.FromResult(Reply(new(true, null, null, null, null, null)));
		});
		var first = Render<SchemaApplicationHost>(p => p.Add(x => x.Document, Screen("form", "First")));
		var second = Render<SchemaApplicationHost>(p => p.Add(x => x.Document, Screen("form", "Second")));
		await first.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await second.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await first.InvokeAsync(() => first.Render(p => p.Add(x => x.Document, Screen("form", "Other record"))));
		await first.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await Assert.That(instances.Distinct().Count()).IsEqualTo(3);
	}

	[Test]
	public async Task TimedOutActionShowsErrorAndAdmitsRetry()
	{
		var attempts = 0;
		Configure(_ => ++attempts == 1 ? throw new TaskCanceledException("transport timeout") : Task.FromResult(Reply(new(true, null, null, Screen("view", "Retried"), null, null))));
		var cut = Render<SchemaApplicationHost>(p => p.Add(x => x.Document, Screen("form", "Edit")));
		await cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await Assert.That(cut.Markup).Contains("could not be sent");
		await cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await Assert.That(cut.Markup).Contains("Retried");
		await Assert.That(attempts).IsEqualTo(2);
	}

	[Test]
	public async Task CharacterSwitchRetiresDelayedPrivateResponseImmediately()
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		var auth = Configure(_ => pending.Task);
		var cut = Render<SchemaApplicationHost>(p => p.Add(x => x.Document, Screen("form", "Private actor")));
		var click = cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await cut.InvokeAsync(() => auth.SetActiveCharacter(new(2, 2, "Other actor", "")));
		pending.SetResult(Reply(new(true, null, null, Screen("form", "Stale actor data"), null, null)));
		await click;
		await Assert.That(cut.Markup).DoesNotContain("Private actor").And.DoesNotContain("Stale actor data");
	}

	[Test]
	public async Task FormToViewAndFailedViewToForm_RenderResultingKindAndErrors()
	{
		var responses = new Queue<SchemaActionResult>([
			new(true, null, null, Screen("view", "Review"), null, null),
			new(false, new Dictionary<string, string> { ["removed"] = "Bucket closed" }, null, Screen("form", "Edit again"), null, null),
			new(false, new Dictionary<string, string> { ["removed"] = "No visible fields" }, null, Screen("form", "Empty") with { Pages = null }, null, null)]);
		Configure(_ => Task.FromResult(Reply(responses.Dequeue())));
		var cut = Render<SchemaApplicationHost>(p => p.Add(x => x.Document, Screen("form", "Edit")));
		await cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await Assert.That(cut.FindComponents<SchemaViewRenderer>().Count).IsEqualTo(1);
		await Assert.That(cut.FindComponents<SchemaFormRenderer>().Count).IsEqualTo(0);
		await cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await Assert.That(cut.FindComponents<SchemaFormRenderer>().Count).IsEqualTo(1);
		await Assert.That(cut.Markup).Contains("Bucket closed");
		await Assert.That(cut.Find("input").GetAttribute("value")).IsEqualTo("draft");
		await cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await Assert.That(cut.Markup).Contains("No visible fields");
	}

	[Test]
	public async Task ReplacedInstance_IgnoresDelayedResponse()
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		var sent = 0;
		Configure(_ => { sent++; return pending.Task; });
		var cut = Render<SchemaApplicationHost>(p => p.Add(x => x.Document, Screen("form", "Old")));
		var click = cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await Assert.That(sent).IsEqualTo(1);
		await Assert.That(cut.Find("fieldset").HasAttribute("disabled")).IsTrue();
		await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.Document, Screen("view", "Current"))));
		pending.SetResult(Reply(new(true, null, null, Screen("form", "Stale private record"), null, null)));
		await click;
		await Assert.That(cut.Markup).Contains("Current");
		await Assert.That(cut.Markup).DoesNotContain("Stale private record");
	}
}
