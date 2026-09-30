using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The profile banner reads profile-handler's portal-known fields (spec §3): image, banner and
/// color, plus role when a game adds it. Fields arrive either as plain strings or as the handler's
/// <c>{ value, visible }</c> shape; a colour that is not <c>#rrggbb</c> never reaches a style.
/// </summary>
public class CharacterProfileServiceTests
{
	private sealed class Handler(Func<string, string?> answer) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = answer(request.RequestUri!.PathAndQuery);
			return Task.FromResult(body is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
		}
	}

	private const string Characters = """[{"name":"Tomas Reyes","objid":"#312:1718000000","created":1718000000,"category":"","image":"/api/wiki-assets/a/tomas.jpg"}]""";

	private static CharacterProfileService Build(Func<string, string?> answer)
	{
		var http = new HttpClient(new Handler(answer)) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(http);
		return new CharacterProfileService(factory, new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance));
	}

	[Test]
	public async Task ReadsImageBannerColorAndRole_InEitherFieldShape()
	{
		var service = Build(path => path switch
		{
			"/http/characters" => Characters,
			"/http/profile?objid=%23312%3A1718000000" => """
				{"character":"Tomas Reyes","objid":"#312:1718000000","dbref":"#312",
				 "fields":{"image":{"value":"/api/wiki-assets/a/tomas.jpg","visible":true},"banner":"/api/wiki-assets/b/docks.jpg",
				           "color":{"value":"#ffb454","visible":true},"role":"Lamplighter · Guild of Lamplighters"}}
				""",
			_ => null,
		});

		var profile = (await service.GetAsync("tomas reyes")).Expect<CharacterProfileData>();
		await Assert.That(profile.Name).IsEqualTo("Tomas Reyes");
		await Assert.That(profile.Dbref).IsEqualTo("#312");
		await Assert.That(profile.Image).IsEqualTo("/api/wiki-assets/a/tomas.jpg");
		await Assert.That(profile.Banner).IsEqualTo("/api/wiki-assets/b/docks.jpg");
		await Assert.That(profile.Color).IsEqualTo("#ffb454");
		await Assert.That(profile.Role).IsEqualTo("Lamplighter · Guild of Lamplighters");
	}

	[Test]
	public async Task AColourThatIsNotHex_IsDropped_AndBlankFieldsAreNull()
	{
		var service = Build(path => path switch
		{
			"/http/characters" => Characters,
			_ when path.StartsWith("/http/profile", StringComparison.Ordinal) => """
				{"character":"Tomas Reyes","objid":"#312:1718000000","dbref":"#312",
				 "fields":{"image":"","banner":{"value":"","visible":true},"color":"red;background:url(x)"}}
				""",
			_ => null,
		});

		var profile = (await service.GetAsync("Tomas Reyes")).Expect<CharacterProfileData>();
		await Assert.That(profile.Color).IsNull();
		await Assert.That(profile.Image).IsNull();
		await Assert.That(profile.Banner).IsNull();
		await Assert.That(profile.Role).IsNull();
	}

	[Test]
	public async Task NoSuchCharacter_IsNotFound_AndAnUnreadableDirectoryIsNot()
	{
		var missing = await Build(path => path == "/http/characters" ? Characters : null).GetAsync("Nobody");
		await Assert.That(missing.Expect<ApiFailure>().Kind).IsEqualTo(ApiFailureKind.NotFound);

		var down = await Build(_ => null).GetAsync("Tomas Reyes");
		await Assert.That(down.Expect<ApiFailure>().Kind).IsNotEqualTo(ApiFailureKind.NotFound)
			.Because("\"no such character\" is a 404 page; \"we could not ask the game\" is not");
	}

	[Test]
	public async Task DirectoryRows_CarryTheImage()
	{
		var directory = new CharacterDirectoryService(
			Substitute.For<IHttpClientFactory>().Tap(f => f.CreateClient(Arg.Any<string>()).Returns(
				new HttpClient(new Handler(p => p == "/http/characters" ? Characters : null)) { BaseAddress = new Uri("https://localhost:8081/") })),
			NullLogger<CharacterDirectoryService>.Instance);
		var rows = (await directory.ListAsync()).Expect<IReadOnlyList<CharacterDirectoryService.CharacterSummary>>();
		await Assert.That(rows[0].Image).IsEqualTo("/api/wiki-assets/a/tomas.jpg");
	}
}

file static class SubstituteExtensions
{
	public static T Tap<T>(this T value, Action<T> action)
	{
		action(value);
		return value;
	}
}
