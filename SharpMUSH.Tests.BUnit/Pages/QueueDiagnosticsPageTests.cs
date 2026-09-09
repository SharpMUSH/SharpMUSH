using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class QueueDiagnosticsPageTests : TrackingBunitContext
{
	private sealed class Api : HttpMessageHandler
	{
		public bool Denied;
		public bool CanProfile;
		public bool Recording;
		public string? StartBody;
		public string? StopQuery;
		public string? LastQuery;
		public Guid? Cursor;
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			if (request.RequestUri!.AbsolutePath == "/api/account/characters")
				return new HttpResponseMessage(HttpStatusCode.OK)
				{ Content = JsonContent.Create(new[] { new AccountAuthService.CharacterSummary(7, 123, "Linked", "") }) };
			LastQuery = request.RequestUri.Query;
			if (Denied) return new(HttpStatusCode.Forbidden);
			if (request.Method == HttpMethod.Post)
			{
				StartBody = await request.Content!.ReadAsStringAsync(ct);
				Recording = true;
				return new(HttpStatusCode.OK);
			}
			if (request.Method == HttpMethod.Delete)
			{
				StopQuery = LastQuery;
				Recording = false;
				return new(HttpStatusCode.NoContent);
			}
			return new(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new QueueDiagnosticsReport([], [new DiagnosticQueueRow(9,
					"#7:123", "#7:123", "enqueue", "Completed", "<script>hidden()</script>", DateTimeOffset.UnixEpoch,
					null, DateTimeOffset.UnixEpoch, TimeSpan.Zero, null, 0, 0)], StartBody is null ? null
					: new DiagnosticProfileReport(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), Recording,
						[new("#7:123", "#7:123", "ACTION", "Function", "ADD", 3, 0, 2.5, 1)]), CanProfile, Cursor, false))
			};
		}
	}
	private Api Setup()
	{
		var api = new Api();
		var client = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient("api").Returns(client);
		Services.AddSingleton(factory).AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(sp => new AccountAuthService(factory, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()));
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("token");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("account");
		return api;
	}

	[Test]
	public async Task SelectedFullIdentityIsSentAndUntrustedMetadataIsEscaped()
	{
		var api = Setup();
		var cut = Render<QueueDiagnostics>();
		cut.WaitForAssertion(() => cut.Find("option[value='#7:123']"));
		await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "#7:123" });
		await Assert.That(api.LastQuery).Contains("character=%237%3A123");
		await Assert.That(cut.FindAll("script").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains("&lt;script&gt;");
		await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Contains("DiagStart"))).IsFalse();
	}

	[Test]
	public async Task OlderHistoryForwardsTheOpaqueCursorUnchanged()
	{
		var api = Setup(); api.Cursor = Guid.NewGuid();
		var cut = Render<QueueDiagnostics>();
		cut.WaitForAssertion(() => cut.Find("option[value='#7:123']"));
		await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "#7:123" });
		await cut.FindAll("button").Single(button => button.TextContent == "DiagOlder").ClickAsync(new());
		await Assert.That(api.LastQuery).Contains($"beforeCursor={api.Cursor}");
		await Assert.That(api.LastQuery).DoesNotContain("beforeSequence");
	}

	[Test]
	public async Task RevokedInspectionClearsPreviouslyVisibleRows()
	{
		var api = Setup(); var cut = Render<QueueDiagnostics>();
		cut.WaitForAssertion(() => cut.Find("option[value='#7:123']"));
		await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "#7:123" });
		await Assert.That(cut.FindAll("tbody tr").Count).IsEqualTo(1);
		api.Denied = true;
		await cut.FindAll("button").Single(button => button.TextContent == "Refresh").ClickAsync(new());
		await Assert.That(cut.FindAll("tbody tr").Count).IsEqualTo(0);
		await Assert.That(cut.Find("[role=alert]").TextContent).IsEqualTo("DiagDenied");
	}
	[Test]
	public async Task ProfileControlsSendFullCharacterAndRenderStoppedResults()
	{
		var api = Setup(); api.CanProfile = true;
		var cut = Render<QueueDiagnostics>();
		cut.WaitForAssertion(() => cut.Find("option[value='#7:123']"));
		await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "#7:123" });
		await cut.Find("input[type=number]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "45" });
		await cut.FindAll("button").Single(button => button.TextContent == "DiagStart").ClickAsync(new());
		using var payload = System.Text.Json.JsonDocument.Parse(api.StartBody!);
		await Assert.That(payload.RootElement.GetProperty("character").GetString()).IsEqualTo("#7:123");
		await Assert.That(payload.RootElement.GetProperty("seconds").GetInt32()).IsEqualTo(45);
		await Assert.That(cut.Markup).Contains("ADD");
		await cut.FindAll("button").Single(button => button.TextContent == "DiagStop").ClickAsync(new());
		await Assert.That(api.StopQuery).Contains("character=%237%3A123");
		await Assert.That(cut.Markup).Contains("DiagStopped");
		await Assert.That(cut.FindAll("button").Any(button => button.TextContent == "DiagStop")).IsFalse();
	}

}
