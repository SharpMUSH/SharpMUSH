using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The Attribute keys page lists the attribute table's keys and sets both of an attribute's keys at once, saying why when
/// the server refuses one.
/// </summary>
public class AttributeKeysPageTests : TrackingBunitContext
{
	private sealed class Api : HttpMessageHandler
	{
		public readonly List<AttributeKeyEntryDto> Entries = [new("XP", null, "ROLE^xpclerk"), new("NOTES", "=#22", null)];
		public readonly List<(string Path, string Body)> Puts = [];
		public HttpStatusCode? Refuse;
		public const string RefusalReason = "I don't understand that key.";
		public bool Forbidden;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			if (Forbidden) return new(HttpStatusCode.Forbidden);

			if (request.Method == HttpMethod.Put)
			{
				Puts.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
				return Refuse is { } status
					? new(status) { Content = JsonContent.Create(new ApiErrorDto(RefusalReason)) }
					: new(HttpStatusCode.NoContent);
			}

			return new(HttpStatusCode.OK) { Content = JsonContent.Create(Entries) };
		}
	}

	private readonly Api _api = new();

	public AttributeKeysPageTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<AttributeKeysService>()
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<AttributeKeys> Loaded()
	{
		var cut = Render<AttributeKeys>();
		cut.WaitForAssertion(() => cut.Find(".attrkey-rows"));
		return cut;
	}

	private static async Task Type(IRenderedComponent<AttributeKeys> cut, string field, string value)
		=> await cut.Find($".{field} input").InputAsync(new ChangeEventArgs { Value = value });

	[Test]
	public async Task EachKeyedAttributeShowsItsKeys()
	{
		var cut = Loaded();

		var rows = cut.FindAll(".attrkey-row");
		await Assert.That(rows.Count).IsEqualTo(2);
		await Assert.That(rows[0].TextContent).Contains("XP");
		await Assert.That(rows[0].TextContent).Contains("Write key: ROLE^xpclerk");
		await Assert.That(rows[0].TextContent).DoesNotContain("Read key");
		await Assert.That(rows[1].TextContent).Contains("Read key: =#22");
	}

	[Test]
	public async Task SavingSetsBothKeysOfTheAttribute()
	{
		var cut = Loaded();

		await Type(cut, "attrkey-name", "level");
		await Type(cut, "attrkey-write", "role^xpclerk");
		await cut.Find(".attrkey-save").ClickAsync(new MouseEventArgs());

		cut.WaitForState(() => _api.Puts.Count == 2);
		await Assert.That(_api.Puts[0].Path).IsEqualTo("/api/admin/attribute-keys/LEVEL/read");
		await Assert.That(_api.Puts[0].Body).Contains("\"key\":\"\"");
		await Assert.That(_api.Puts[1].Path).IsEqualTo("/api/admin/attribute-keys/LEVEL/write");
		await Assert.That(_api.Puts[1].Body).Contains("role^xpclerk");
	}

	[Test]
	public async Task NamingAKeyedAttributeFillsInItsKeys()
	{
		var cut = Loaded();

		await Type(cut, "attrkey-name", "xp");

		await Assert.That(cut.Find(".attrkey-write input").GetAttribute("value")).IsEqualTo("ROLE^xpclerk");
		await Assert.That(cut.Find(".attrkey-read input").GetAttribute("value")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ARefusalSaysWhyAndStopsBeforeTheOtherKey()
	{
		_api.Refuse = HttpStatusCode.BadRequest;
		var cut = Loaded();

		await Type(cut, "attrkey-name", "LEVEL");
		await Type(cut, "attrkey-read", ")(");
		await cut.Find(".attrkey-save").ClickAsync(new MouseEventArgs());

		var snackbar = Services.GetRequiredService<ISnackbar>();
		cut.WaitForState(() => snackbar.ShownSnackbars.Any());
		await Assert.That(snackbar.ShownSnackbars.Single().Message).Contains("I don't understand that key.");
		await Assert.That(_api.Puts.Count).IsEqualTo(1);
		await Assert.That(cut.Find(".attrkey-read input").GetAttribute("value")).IsEqualTo(")(")
			.Because("a refused key stays in the form to be fixed");
	}

	[Test]
	public async Task ClearingTakesOffBothKeys()
	{
		var cut = Loaded();

		await cut.FindAll(".attrkey-row")[0].QuerySelector(".mud-icon-button[title='Clear both keys']")!.ClickAsync(new MouseEventArgs());

		cut.WaitForState(() => _api.Puts.Count == 2);
		await Assert.That(_api.Puts.Select(put => put.Path)).IsEquivalentTo(
			["/api/admin/attribute-keys/XP/read", "/api/admin/attribute-keys/XP/write"]);
		await Assert.That(_api.Puts.All(put => put.Body.Contains("\"key\":\"\""))).IsTrue();
	}

	[Test]
	public async Task SomeoneWithoutConfigAdminIsTold()
	{
		_api.Forbidden = true;
		var cut = Render<AttributeKeys>();

		cut.WaitForAssertion(() => cut.Find(".attrkey-empty"));
		await Assert.That(cut.Find(".attrkey-empty").TextContent).IsEqualTo("Attribute keys need config.admin.");
	}
}
