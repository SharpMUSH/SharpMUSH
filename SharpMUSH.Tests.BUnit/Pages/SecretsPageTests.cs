using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The Secrets page lists where each value comes from and sends a value up once: the value field is a password field the
/// browser does not offer to save, and it is emptied after a save.
/// </summary>
public class SecretsPageTests : TrackingBunitContext
{
	private sealed class Api : HttpMessageHandler
	{
		public readonly List<SecretStatus> Secrets =
		[
			new("discord.token", "The Discord bot's token.", SecretSource.File, "/run/secrets/discord.token", false, null, null,
				"discord.admin", true),
			new("sql.password", "The password of sql_username on the SQL server.", SecretSource.Stored, null, true,
				DateTimeOffset.UnixEpoch, new SecretAuthor("#1:1", "God"), null, true),
			new("http.github.token", null, SecretSource.Stored, null, true,
				DateTimeOffset.UnixEpoch, new SecretAuthor("#1:1", "God"), null, false),
			new("http.weather.key", null, SecretSource.Stored, null, true,
				DateTimeOffset.UnixEpoch, new SecretAuthor("#1:1", "God"), null, false, "api.weather.example"),
		];

		public readonly List<(string Path, string Body)> Puts = [];
		public string? PutPath;
		public string? PutBody;
		public string? DeletePath;
		public bool Forbidden;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			if (Forbidden)
			{
				return new(HttpStatusCode.Forbidden);
			}

			if (request.Method == HttpMethod.Put)
			{
				PutPath = request.RequestUri!.AbsolutePath;
				PutBody = await request.Content!.ReadAsStringAsync(ct);
				Puts.Add((PutPath, PutBody));
				var name = Uri.UnescapeDataString(PutPath.Split('/')[3]);
				var secret = Secrets.FirstOrDefault(s => s.Name == name) ?? Secrets[1] with { Name = name };
				return new(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(PutPath.EndsWith("/hosts", StringComparison.Ordinal)
						? secret with { Hosts = string.Join(' ', System.Text.Json.JsonDocument.Parse(PutBody).RootElement.GetProperty("hosts").EnumerateArray().Select(h => h.GetString())) }
						: secret)
				};
			}

			if (request.Method == HttpMethod.Delete)
			{
				DeletePath = request.RequestUri!.AbsolutePath;
				return new(HttpStatusCode.OK) { Content = JsonContent.Create(Secrets[1] with { Source = SecretSource.Unset, Stored = false }) };
			}

			return new(HttpStatusCode.OK) { Content = JsonContent.Create(Secrets) };
		}
	}

	private readonly Api _api = new();

	public SecretsPageTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<SecretsService>()
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task EachSecretShowsWhereItsValueComesFrom()
	{
		var cut = Render<Secrets>();
		cut.WaitForAssertion(() => cut.Find(".secret-rows"));

		var rows = cut.FindAll(".secret-row");
		await Assert.That(rows.Count).IsEqualTo(4);
		await Assert.That(rows[0].TextContent).Contains("set by /run/secrets/discord.token");
		await Assert.That(rows[1].TextContent).Contains("stored");
		await Assert.That(rows[1].TextContent).Contains("God");
		await Assert.That(rows[0].QuerySelector(".mud-icon-button[title='Clear the stored value']")).IsNull()
			.Because("nothing is stored to clear while only a file sets it");
	}

	[Test]
	public async Task SavingSendsTheValueOnceAndEmptiesTheField()
	{
		var cut = Render<Secrets>();
		cut.WaitForAssertion(() => cut.Find(".secret-rows"));

		var inputs = cut.FindAll(".secret-form input");
		await Assert.That(inputs[1].GetAttribute("type")).IsEqualTo("password");
		await Assert.That(inputs[1].GetAttribute("autocomplete")).IsEqualTo("new-password");

		await inputs[0].InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "sql.password" });
		await cut.FindAll(".secret-form input")[1].InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "hunter2" });
		await cut.Find(".secret-save").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

		cut.WaitForState(() => _api.PutPath is not null);
		await Assert.That(_api.PutPath).IsEqualTo("/api/secrets/sql.password");
		await Assert.That(_api.PutBody).Contains("hunter2");
		cut.WaitForState(() => string.IsNullOrEmpty(cut.FindAll(".secret-form input")[1].GetAttribute("value")));
	}

	[Test]
	public async Task AnHttpSecretWithNoHostsIsFlagged()
	{
		var cut = Render<Secrets>();
		cut.WaitForAssertion(() => cut.Find(".secret-rows"));

		var rows = cut.FindAll(".secret-row");
		await Assert.That(rows[2].QuerySelector(".secret-no-hosts")).IsNotNull();
		await Assert.That(rows[2].TextContent).Contains("@http sends it nowhere until you name its hosts.");
		await Assert.That(rows[3].QuerySelector(".secret-no-hosts")).IsNull();
		await Assert.That(rows[3].TextContent).Contains("@http may send it to api.weather.example");
		await Assert.That(rows[1].QuerySelector(".secret-no-hosts")).IsNull()
			.Because("@http never sends a secret outside http.*, so sql.password needs no hosts");
		await Assert.That(rows[1].QuerySelector(".mud-icon-button[title='Hosts @http may send it to']")).IsNull();
	}

	[Test]
	public async Task EditingHostsSendsTheList()
	{
		var cut = Render<Secrets>();
		cut.WaitForAssertion(() => cut.Find(".secret-rows"));

		await cut.FindAll(".secret-row")[2].QuerySelector(".mud-icon-button[title='Hosts @http may send it to']")!
			.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
		// The space ends the first host; the second is added when the field loses focus to Save.
		await cut.Find(".secret-hosts-form .list-editor-input").InputAsync(
			new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "API.github.com  uploads.github.com" });
		await cut.Find(".secret-hosts-form .list-editor-input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs());
		await Assert.That(cut.FindAll(".secret-hosts-form .list-editor-chip").Select(chip => chip.FirstChild!.TextContent.Trim()))
			.IsEquivalentTo(new[] { "api.github.com", "uploads.github.com" });
		await cut.Find(".secret-hosts-save").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

		cut.WaitForState(() => _api.PutPath is not null);
		await Assert.That(_api.PutPath).IsEqualTo("/api/secrets/http.github.token/hosts");
		await Assert.That(_api.PutBody).IsEqualTo("""{"hosts":["api.github.com","uploads.github.com"]}""");
		cut.WaitForState(() => cut.FindAll(".secret-hosts-form").Count == 0);
	}

	[Test]
	public async Task TheFormAsksForHostsWithAnHttpSecret()
	{
		var cut = Render<Secrets>();
		cut.WaitForAssertion(() => cut.Find(".secret-rows"));

		await cut.FindAll(".secret-form input")[0].InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "sql.password" });
		await Assert.That(cut.FindAll(".secret-form input").Count).IsEqualTo(2);
		await Assert.That(cut.FindAll(".secret-hosts-editor")).IsEmpty();

		await cut.FindAll(".secret-form input")[0].InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "http.github.token" });
		await Assert.That(cut.FindAll(".secret-form input").Count).IsEqualTo(3);
		await Assert.That(cut.Find(".secret-hosts-editor .list-editor-help").TextContent).Contains("only to the hosts named for it");

		await cut.FindAll(".secret-form input")[1].InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "ghp_example" });
		await cut.Find(".secret-form .list-editor-input").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "api.github.com" });
		await cut.Find(".secret-form .list-editor-input").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
		await cut.Find(".secret-save").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

		cut.WaitForState(() => _api.Puts.Count == 2);
		await Assert.That(_api.Puts[0].Path).IsEqualTo("/api/secrets/http.github.token");
		await Assert.That(_api.Puts[1]).IsEqualTo(("/api/secrets/http.github.token/hosts", """{"hosts":["api.github.com"]}"""));
	}

	[Test]
	public async Task SomeoneWhoMayManageNoSecretIsToldSo()
	{
		_api.Forbidden = true;

		var cut = Render<Secrets>();
		cut.WaitForAssertion(() => cut.Find(".secret-forbidden"));

		await Assert.That(cut.FindAll(".secret-form")).IsEmpty();
	}

	[Test]
	public async Task ThePageAsksOnlyForASignedInAccount()
	{
		var authorize = typeof(Secrets).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
			.Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();

		await Assert.That(authorize.Policy).IsNull()
			.Because("a secret's ManagedBy permission lets its holder manage it here; the server filters the list");
	}
}
