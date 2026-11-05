using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The portal speaks the acting character's language: choosing one sets the character's <c>LOCALE</c> with
/// <c>@locale</c>, and a character that already carries one brings it to a tab that does not.
/// </summary>
public class CharacterLanguageTests : TrackingBunitContext
{
	private readonly IAccountAuthState _account = Substitute.For<IAccountAuthState>();
	private readonly CommandsHandler _commands = new();

	private CharacterLanguage Arrange(AccountAuthService.CharacterSummary? acting)
	{
		_account.ActiveCharacter.Returns(acting);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(Track(new HttpClient(_commands) { BaseAddress = new Uri("https://localhost:8081/") }));
		JSInterop.Mode = JSRuntimeMode.Loose;
		return new CharacterLanguage(_account, new GameCommandService(factory), JSInterop.JSRuntime,
			Services.GetRequiredService<NavigationManager>(), NullLogger<CharacterLanguage>.Instance);
	}

	private static AccountAuthService.CharacterSummary Sailor(string? locale = null)
		=> new(5, 100, "Sailor", "", IsActing: true, Locale: locale);

	private BunitNavigationManager Navigation => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

	private IReadOnlyList<string?> Stored()
		=> JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem" && Equals(i.Arguments[0], CharacterLanguage.StorageKey))
			.Select(i => i.Arguments[1] as string).ToList();

	private bool Reloaded => Navigation.History.Any(entry => entry.Options.ForceLoad);

	[Test]
	public async Task Choosing_a_language_sets_the_characters_locale_then_reloads_in_it()
	{
		using var culture = CultureScope.For("en");
		var language = Arrange(Sailor());

		await Assert.That(await language.ChooseAsync("de")).IsTrue();

		await Assert.That(_commands.Sent).IsEquivalentTo(["@locale de"]);
		await Assert.That(Stored()).IsEquivalentTo(["de"]);
		await Assert.That(Reloaded).IsTrue();
	}

	[Test]
	public async Task A_refused_locale_leaves_the_portal_as_it_was()
	{
		using var culture = CultureScope.For("en");
		_commands.Status = HttpStatusCode.TooManyRequests;
		var language = Arrange(Sailor());

		await Assert.That(await language.ChooseAsync("de")).IsFalse();

		await Assert.That(Stored()).IsEmpty().Because("the next load would take the character's old language back");
		await Assert.That(Reloaded).IsFalse();
	}

	[Test]
	public async Task Without_a_character_the_language_stays_in_the_browser()
	{
		using var culture = CultureScope.For("en");
		var language = Arrange(null);

		await Assert.That(await language.ChooseAsync("fr")).IsTrue();

		await Assert.That(_commands.Sent).IsEmpty();
		await Assert.That(Stored()).IsEquivalentTo(["fr"]);
	}

	[Test]
	public async Task A_character_with_a_locale_brings_it_to_the_tab()
	{
		using var culture = CultureScope.For("en");
		var language = Arrange(null);

		_account.ActiveCharacter.Returns(Sailor("de-DE"));
		_account.ActiveCharacterChanged += Raise.Event<Action>();

		await Assert.That(Stored()).IsEquivalentTo(["de"]).Because("de serves de-DE");
		await Assert.That(Reloaded).IsTrue();
		await Assert.That(_commands.Sent).IsEmpty().Because("following the character writes nothing back");
		language.Dispose();
	}

	[Test]
	[Arguments("fr")]
	[Arguments("ja")]
	[Arguments(null)]
	public async Task A_tab_already_in_the_characters_language_or_one_the_portal_lacks_stays(string? locale)
	{
		using var culture = CultureScope.For("fr");
		var language = Arrange(null);

		_account.ActiveCharacter.Returns(Sailor(locale));
		_account.ActiveCharacterChanged += Raise.Event<Action>();

		await Assert.That(Stored()).IsEmpty();
		await Assert.That(Reloaded).IsFalse();
		language.Dispose();
	}

	[Test]
	[Arguments("de-DE", "de")]
	[Arguments("pt-BR", "pt-BR")]
	[Arguments("zh-CN", "zh-Hans")]
	[Arguments("EN", "en")]
	[Arguments("pt-PT", null)]
	[Arguments("ja", null)]
	[Arguments("not a tag", null)]
	[Arguments("", null)]
	public async Task A_game_locale_is_served_by_its_nearest_portal_locale(string tag, string? expected)
	{
		await Assert.That(PortalLocales.Match(tag)).IsEqualTo(expected);
	}

	/// <summary>Answers <c>POST api/commands</c>, recording each command line.</summary>
	private sealed class CommandsHandler : HttpMessageHandler
	{
		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
		public List<string> Sent { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var sent = await request.Content!.ReadFromJsonAsync<PortalCommandRequest>(cancellationToken);
			if (Status != HttpStatusCode.OK)
				return new HttpResponseMessage(Status);

			Sent.Add(sent!.Command);
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PortalCommandResponse(["Locale set to de."], null, false)) };
		}
	}
}
