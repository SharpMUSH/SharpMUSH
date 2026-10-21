using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@gender</c> sets the attribute <c>gender_attr</c> names, through the same write as <c>@sex</c> and
/// every other <c>@&lt;attribute&gt;</c> command. The session's world is configured from the test
/// <c>mushcnf.dst</c>, whose <c>gender_attr</c> is GENDER, so here <c>@gender</c> and <c>@sex</c> set
/// different attributes.
/// </summary>
public class GenderCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private string Configured => AttributeHelpers.GenderAttributeName(WebAppFactoryArg.Services
		.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Attribute.GenderAttribute);

	private sealed record Mortal(TestIsolationHelpers.TestPlayer Player, IMUSHCodeParser Parser);

	private async Task<Mortal> MortalInOwnRoom(string token)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Gender");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var dig = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig Room_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@tel me={dig.Message.ToPlainText().Trim()}"));
		return new Mortal(player, parser);
	}

	/// <summary>Everything <paramref name="who"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<List<string>> Run(Mortal who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountForHandle(who.Player.Handle);
		await who.Parser.CommandParse(who.Player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.ForHandle(who.Player.Handle).Skip(before)];
	}

	/// <summary>The attribute's raw value as God reads it, or empty when it is not set.</summary>
	private async Task<string> GodGet(DBRef who, string attribute)
		=> (await GodParser.EvaluateAsync(MarkupText.Plain($"[get(#{who.Number}/{attribute})]"))).ToPlainText();

	[Test]
	public async Task TheShippedGenderAttributeIsSexAndABlankOneFallsBackToIt()
	{
		await Assert.That(AttributeHelpers.GenderAttributeName(SharpMUSHOptions.Default().Attribute.GenderAttribute))
			.IsEqualTo("SEX");
		await Assert.That(AttributeHelpers.GenderAttributeName(null)).IsEqualTo("SEX");
		await Assert.That(AttributeHelpers.GenderAttributeName("")).IsEqualTo("SEX");
		await Assert.That(AttributeHelpers.GenderAttributeName("  ")).IsEqualTo("SEX");
		await Assert.That(AttributeHelpers.GenderAttributeName("pronoun")).IsEqualTo("PRONOUN");
	}

	[Test]
	public async Task SetsTheConfiguredAttributeThatPronounsRead()
	{
		await Assert.That(Configured).IsEqualTo("GENDER").Because("the test mushcnf.dst sets gender_attr GENDER");
		var token = TestIsolationHelpers.GenerateUniqueName("gset");
		var who = await MortalInOwnRoom(token);

		var heard = await Run(who, "@gender me=Female");

		await Assert.That(heard).Contains($"{who.Player.Name}/GENDER - Set.");
		await Assert.That(await GodGet(who.Player.DbRef, "GENDER")).IsEqualTo("Female");
		await Assert.That(await GodGet(who.Player.DbRef, "SEX")).IsEqualTo(string.Empty);
		var pronouns = await who.Parser.CommandParse(who.Player.Handle, ConnectionService,
			MarkupText.Plain($"think {token}:[subj(me)]:%s:%o:%p"));
		await Assert.That(pronouns.Message.ToPlainText()).IsEqualTo($"{token}:she:she:her:her");
	}

	/// <summary><c>@sex</c> stays PennMUSH's: it sets SEX whatever <c>gender_attr</c> says.</summary>
	[Test]
	public async Task SexStillSetsSex()
	{
		var who = await MortalInOwnRoom(TestIsolationHelpers.GenerateUniqueName("gsex"));

		var heard = await Run(who, "@sex me=Male");

		await Assert.That(heard).Contains($"{who.Player.Name}/SEX - Set.");
		await Assert.That(await GodGet(who.Player.DbRef, "SEX")).IsEqualTo("Male");
		await Assert.That(await GodGet(who.Player.DbRef, "GENDER")).IsEqualTo(string.Empty);
	}

	/// <summary>The value is stored as typed, unevaluated and with its braces, exactly as <c>@sex</c> stores it.</summary>
	[Test]
	public async Task StoresTheValueAsSexDoes()
	{
		var who = await MortalInOwnRoom(TestIsolationHelpers.GenerateUniqueName("gval"));
		const string value = "[add(1,2)] %n {braced, text} \\escaped";

		await Run(who, $"@gender me={value}");
		await Run(who, $"@sex me={value}");

		var stored = await GodGet(who.Player.DbRef, "GENDER");
		await Assert.That(stored).IsEqualTo(await GodGet(who.Player.DbRef, "SEX"));
		await Assert.That(stored).Contains("[add(1,2)]");
	}

	[Test]
	public async Task WithNoValueItClearsTheAttribute()
	{
		var who = await MortalInOwnRoom(TestIsolationHelpers.GenerateUniqueName("gclr"));
		await Run(who, "@gender me=Male");

		var heard = await Run(who, "@gender me");

		await Assert.That(heard).Contains($"{who.Player.Name}/GENDER - Cleared.");
		await Assert.That(await GodGet(who.Player.DbRef, "GENDER")).IsEqualTo(string.Empty);
	}

	/// <summary>A mortal cannot set another player's gender, and is told what <c>@sex</c> would tell them.</summary>
	[Test]
	public async Task RefusesAnObjectYouDoNotControl()
	{
		var who = await MortalInOwnRoom(TestIsolationHelpers.GenerateUniqueName("gden"));
		var other = await MortalInOwnRoom(TestIsolationHelpers.GenerateUniqueName("gtgt"));

		var genderHeard = await Run(who, $"@gender #{other.Player.DbRef.Number}=Male");
		var sexHeard = await Run(who, $"@sex #{other.Player.DbRef.Number}=Male");

		await Assert.That(await GodGet(other.Player.DbRef, "GENDER")).IsEqualTo(string.Empty);
		await Assert.That(await GodGet(other.Player.DbRef, "SEX")).IsEqualTo(string.Empty);
		await Assert.That(genderHeard).IsNotEmpty();
		await Assert.That(genderHeard).IsEquivalentTo(sexHeard);
	}

	[Test]
	public async Task AQuietMortalIsNotToldItWasSet()
	{
		var who = await MortalInOwnRoom(TestIsolationHelpers.GenerateUniqueName("gqt"));
		await Run(who, "@set me=QUIET");

		var heard = await Run(who, "@gender me=Female");

		await Assert.That(heard).DoesNotContain($"{who.Player.Name}/GENDER - Set.");
		await Assert.That(await GodGet(who.Player.DbRef, "GENDER")).IsEqualTo("Female");
	}

	[Test]
	public async Task WithNoArgumentsItShowsItsUsage()
	{
		var who = await MortalInOwnRoom(TestIsolationHelpers.GenerateUniqueName("guse"));

		await Assert.That(await Run(who, "@gender")).Contains("Usage: @gender <object>=<value>");
	}

	/// <summary>
	/// Abbreviations follow PennMUSH's prefix table: <c>@ge</c> begins both <c>@gedit</c> and
	/// <c>@gender</c>, so it names neither; one more letter picks each.
	/// </summary>
	[Test]
	public async Task GeNamesNeitherGeditNorGender()
	{
		var trie = CommandTrie.For(GodParser.CommandLibrary);

		await Assert.That(trie.FindAbbreviation("@ge")).IsNull();
		await Assert.That(trie.FindAbbreviation("@ged")?.CommandName).IsEqualTo("@GEDIT").IgnoringCase();
		await Assert.That(trie.FindAbbreviation("@gen")?.CommandName).IsEqualTo("@GENDER").IgnoringCase();
	}

	private static IDisposable GenderAttr(string? attribute) => TestOptionsOverride.Scope(options => options with
	{
		Attribute = options.Attribute with { GenderAttribute = attribute }
	});

	/// <summary>
	/// <c>gender_attr</c> is read each time <c>@gender</c> runs: with the shipped SEX it sets SEX, blank it
	/// falls back to SEX as pronouns do, and pointed elsewhere it sets that attribute, which pronouns then read.
	/// The change is scoped to this test's own async flow, so no other test sees it.
	/// </summary>
	[Test]
	public async Task FollowsGenderAttrAsItStandsWhenItRuns()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("gopt");
		var who = await MortalInOwnRoom(token);

		using (GenderAttr("SEX"))
		{
			await Assert.That(await Run(who, "@gender me=Male")).Contains($"{who.Player.Name}/SEX - Set.");
		}

		await Assert.That(await GodGet(who.Player.DbRef, "SEX")).IsEqualTo("Male");

		using (GenderAttr("  "))
		{
			await Run(who, "@gender me=Female");
		}

		await Assert.That(await GodGet(who.Player.DbRef, "SEX")).IsEqualTo("Female");

		using (GenderAttr("Pronouns"))
		{
			await Assert.That(await Run(who, "@gender me=Male")).Contains($"{who.Player.Name}/PRONOUNS - Set.");
			var pronouns = await who.Parser.CommandParse(who.Player.Handle, ConnectionService,
				MarkupText.Plain($"think {token}:[subj(me)]:%s"));
			await Assert.That(pronouns.Message.ToPlainText()).IsEqualTo($"{token}:he:he");
		}

		await Assert.That(await GodGet(who.Player.DbRef, "PRONOUNS")).IsEqualTo("Male");
		await Assert.That(await GodGet(who.Player.DbRef, "SEX")).IsEqualTo("Female");
	}
}
