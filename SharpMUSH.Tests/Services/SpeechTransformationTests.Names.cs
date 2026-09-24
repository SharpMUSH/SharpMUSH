using MarkupString;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public partial class SpeechTransformationTests
{
	[Test]
	[Arguments("abcdef")]
	[Arguments("a")]
	[Arguments("😀a")]
	[Arguments("a\u0301b")]
	public async Task MonikerRetainsPositionalStylesWithoutSplittingGraphemes(string name)
	{
		var parser = Factory.CommandParser;
		var template = (await parser.FunctionParse(MarkupText.Plain("[ansi(r,x)][ansi(g,y)]")))!.Message!;
		var result = NameFormatter.ApplyMoniker(name, template);
		var first = System.Globalization.StringInfo.GetNextTextElement(name);
		var expected = MarkupText.Concat(MarkupText.Wrap(template.Runs[0].Markups, first),
			MarkupText.Wrap(template.Runs[1].Markups, name[first.Length..]));
		await Assert.That(result.ToPlainText()).IsEqualTo(name);
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo(expected.Render(MarkupFormat.Ansi));
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AutomaticMonikerConfigurationDoesNotDisableExplicitNames(bool enabled)
	{
		var actor = await Player();
		var suffix = Guid.NewGuid().ToString("N")[..12];
		await Admin($"@name {actor.DbRef}=A{suffix}");
		await Admin($"&NAMEACCENT {actor.DbRef}='{new string('-', suffix.Length)}");
		await Admin($"@moniker {actor.DbRef}=[ansi(r,x)][ansi(g,y)]");
		var configuration = new FixedOptions(Options with { Cosmetic = Options.Cosmetic with { Monikers = enabled } });
		var names = new NameFormatter(Attributes, configuration);
		var node = await Node(actor.DbRef);
		var speech = await names.FormatAsync(node, NameContext.Speech);
		await Assert.That(speech.ToPlainText()).IsEqualTo("Á" + suffix);
		await Assert.That(speech.Runs.Length > 0).IsEqualTo(enabled);
		var explicitName = await names.FormatAsync(node, NameContext.UnaccentedMoniker);
		await Assert.That(explicitName.ToPlainText()).IsEqualTo("A" + suffix);
		await Assert.That(explicitName.Runs.Length > 0).IsTrue();
		var functionName = await names.FormatAsync(node, NameContext.AccentedMoniker);
		await Assert.That(functionName.ToPlainText()).IsEqualTo("Á" + suffix);
		await Assert.That(functionName.Runs.Length > 0).IsTrue();
		await Assert.That((await names.FormatAsync(node, NameContext.NoSpoof)).ToPlainText()).IsEqualTo("A" + suffix);
	}

	[Test]
	[Arguments(true, true, "Someone")]
	[Arguments(true, false, "")]
	[Arguments(false, true, "Something")]
	[Arguments(false, false, "Something")]
	public async Task FullInvisibilityUsesDarkLegalAndNeverChangesExplicitNames(bool player, bool canDark, string replacement)
	{
		var actor = await Player();
		var reference = player ? actor.DbRef : await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "InvisibleSpeaker");
		await Flag(reference, "DARK");
		if (canDark) await Admin($"@power {reference}=Can_Dark");
		var node = await Node(reference);
		var configuration = new FixedOptions(Options with { Command = Options.Command with { FullInvisibility = true } });
		var names = new NameFormatter(Attributes, configuration);
		var expected = replacement.Length == 0 ? node.Object().Name : replacement;
		await Assert.That((await names.FormatAsync(node, NameContext.Speech)).ToPlainText()).IsEqualTo(expected);
		await Assert.That((await names.FormatAsync(node, NameContext.NoSpoof)).ToPlainText()).IsEqualTo(expected);
		await Assert.That((await names.FormatAsync(node, NameContext.Accented)).ToPlainText()).IsEqualTo(node.Object().Name);
		await Assert.That((await names.FormatAsync(node, NameContext.UnaccentedMoniker)).ToPlainText()).IsEqualTo(node.Object().Name);
	}

	[Test]
	[Arguments("")]
	[Arguments("an alias")]
	public async Task PlainOrEmptyMonikerIsNotAnAlias(string template)
	{
		await Assert.That(NameFormatter.ApplyMoniker("actual", MarkupText.Plain(template)).ToPlainText()).IsEqualTo("actual");
	}
}
