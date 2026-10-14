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
		];

		public string? PutPath;
		public string? PutBody;
		public string? DeletePath;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			if (request.Method == HttpMethod.Put)
			{
				PutPath = request.RequestUri!.AbsolutePath;
				PutBody = await request.Content!.ReadAsStringAsync(ct);
				return new(HttpStatusCode.OK) { Content = JsonContent.Create(Secrets[1]) };
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
		await Assert.That(rows.Count).IsEqualTo(2);
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
}
