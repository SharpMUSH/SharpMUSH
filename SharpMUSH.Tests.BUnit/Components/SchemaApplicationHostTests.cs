using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

public class SchemaApplicationHostTests : TrackingBunitContext
{
	private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
	}

	private AccountAuthService Configure(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(Track(new HttpClient(new Handler(send)) { BaseAddress = new("https://localhost/") }));
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
	private HttpResponseMessage Reply(SchemaActionResult result) => Track(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: SchemaJson.Options) });

	[Test]
	[Arguments(false, "config")]
	[Arguments(true, "config")]
	[Arguments(false, "actor")]
	[Arguments(true, "actor")]
	[Arguments(false, "dispose")]
	[Arguments(true, "dispose")]
	public async Task WidgetRetiresSchemaAndDataReadsOnConfigurationAndActorChanges(bool delayData, string replacement)
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		var requested = new TaskCompletionSource();
		var admissions = 0;
		var auth = Configure(request =>
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path == (delayData ? "/old-data" : "/old") && Interlocked.Increment(ref admissions) == 1)
			{
				requested.TrySetResult();
				return pending.Task;
			}
			return Task.FromResult(Track(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Screen("view", path), options: SchemaJson.Options) }));
		});
		Services.AddSingleton(new ApplicationCatalog([]))
			.AddSingleton(Substitute.For<IPlayTerminalService>())
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance));
		var cut = Render<SchemaWidget>(p => p.Add(x => x.Config, JsonSerializer.SerializeToElement(new { schemaUrl = "/initial" })));
		var oldLoad = cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
		{
			[nameof(SchemaWidget.Config)] = JsonSerializer.SerializeToElement(new { schemaUrl = "/old", dataUrl = delayData ? "/old-data" : null })
		})));
		await requested.Task;
		if (replacement == "config")
			await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.Config, JsonSerializer.SerializeToElement(new { schemaUrl = "/current" }))));
		else if (replacement == "actor")
			await cut.InvokeAsync(() => auth.SetActiveCharacter(new(2, 2, "Other actor", "")));
		else await DisposeComponentsAsync();
		pending.SetResult(Track(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = delayData
			? new StringContent("{\"fields\":{\"title\":{\"value\":\"OLD PRIVATE RECORD\",\"visible\":true}}}", System.Text.Encoding.UTF8, "application/json")
			: JsonContent.Create(Screen("view", "OLD PRIVATE RECORD"), options: SchemaJson.Options)
		}));
		await oldLoad;
		if (replacement == "dispose") return;
		await Assert.That(cut.Markup).DoesNotContain("OLD PRIVATE RECORD");
		await Assert.That(cut.FindComponent<SchemaApplicationHost>().Instance.Document!.Title).IsEqualTo(replacement == "config" ? "/current" : "/old");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task WidgetProfileContextRetiresBothReadStages(bool delayData)
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>();
		var requested = new TaskCompletionSource();
		Configure(request =>
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path == (delayData ? "/data/Old" : "/schema/Old"))
			{
				requested.TrySetResult();
				return pending.Task;
			}
			return Task.FromResult(Track(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Screen("view", path), options: SchemaJson.Options) }));
		});
		Services.AddSingleton(new ApplicationCatalog([]))
			.AddSingleton(Substitute.For<IPlayTerminalService>())
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance));
		var config = JsonSerializer.SerializeToElement(new { schemaUrl = "/initial" });
		var root = Render<CascadingValue<ProfilePageContext>>(p => p.Add(x => x.Value, new ProfilePageContext("Old", false))
			.AddChildContent<SchemaWidget>(p => p.Add(x => x.Config, config)));
		var cut = root.FindComponent<SchemaWidget>();
		config = JsonSerializer.SerializeToElement(new { schemaUrl = "/schema/{character}", dataUrl = delayData ? "/data/{character}" : null });
		var oldLoad = cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(SchemaWidget.Config)] = config })));
		await requested.Task;
		await root.InvokeAsync(() => root.Render(p => p.Add(x => x.Value, new ProfilePageContext("Current", false))
			.AddChildContent<SchemaWidget>(p => p.Add(x => x.Config, config))));
		pending.SetResult(Track(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = delayData
			? new StringContent("{\"fields\":{\"title\":{\"value\":\"OLD PRIVATE RECORD\",\"visible\":true}}}", System.Text.Encoding.UTF8, "application/json")
			: JsonContent.Create(Screen("view", "OLD PRIVATE RECORD"), options: SchemaJson.Options)
		}));
		await oldLoad;
		await Assert.That(root.Markup).DoesNotContain("OLD PRIVATE RECORD");
		await Assert.That(root.FindComponent<SchemaApplicationHost>().Instance.Document!.Title).IsEqualTo("/schema/Current");
	}

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
	public async Task DisposedHostIgnoresDelayedRedirectAndToast()
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		Configure(_ => pending.Task);
		var navigation = Services.GetRequiredService<NavigationManager>();
		var originalLocation = navigation.Uri;
		var cut = Render<SchemaApplicationHost>(p => p.Add(x => x.Document, Screen("form", "Private actor")));
		var click = cut.FindAll("button").First(b => b.TextContent.Contains("Go")).ClickAsync();
		await DisposeComponentsAsync();
		pending.SetResult(Reply(new(true, null, null, Screen("view", "Retired"), "/apps/retired", "Retired toast")));
		await click;
		await Assert.That(navigation.Uri).IsEqualTo(originalLocation);
		await Assert.That(Services.GetRequiredService<MudBlazor.ISnackbar>().ShownSnackbars.Count()).IsEqualTo(0);
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
