using Mediator;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// An attribute NAME and an attribute PATTERN are different sets.
///
/// A name is PennMUSH's <c>good_atr_name</c>: every character must be in <c>atr_name_table</c>
/// (<c>utils/gentables.c</c>), which is <c>A-Z 0-9</c> and <c>! " # $ &amp; ' * + , - . / ; &lt; = &gt; ? @ _ ` | ~</c>.
/// Penn upper-cases before it checks, so lower case is accepted too. The same table backs flag names,
/// power names, q-register names and user lock names.
///
/// A pattern is what follows the <c>/</c> in <c>obj/attr</c> for <c>@wipe</c>, <c>@cpattr</c>,
/// <c>lattr()</c> and the rest: any valid name, plus the wildcard and regex metacharacters a pattern
/// may carry. Penn does not validate the pattern at all; SharpMUSH still refuses whitespace, <c>/</c>,
/// <c>%</c> and <c>:</c>, none of which can occur in a stored attribute name.
/// </summary>
public class AttributeNameCharacterSetTests
{
	private const string PennAtrNameTable = "!\"#$&'*+,-./0123456789;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ_`|~";

	private static ValidateService Service() => new(
		Substitute.For<IMediator>(),
		Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
		Substitute.For<ILockService>());

	private static async ValueTask<bool> IsValidName(string name)
		=> await Service().Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(name), new None());

	public static IEnumerable<string> PennNameCharacters()
		=> PennAtrNameTable.Where(c => c != '`').Select(c => $"A{c}B");

	[Test]
	[MethodDataSource(nameof(PennNameCharacters))]
	public async Task AcceptsEveryCharacterInPennsAtrNameTable(string name)
		=> await Assert.That(await IsValidName(name)).IsTrue()
			.Because($"'{name}' passes PennMUSH's good_atr_name");

	[Test]
	[Arguments("a%b")]
	[Arguments("a(b")]
	[Arguments("a)b")]
	[Arguments("a:b")]
	[Arguments("a[b")]
	[Arguments("a]b")]
	[Arguments("a^b")]
	[Arguments("a{b")]
	[Arguments("a}b")]
	[Arguments("a\\b")]
	[Arguments("a b")]
	public async Task RejectsCharactersOutsidePennsAtrNameTable(string name)
		=> await Assert.That(await IsValidName(name)).IsFalse()
			.Because($"'{name}' fails PennMUSH's good_atr_name");

	[Test]
	[Arguments("foo")]
	[Arguments("FOO`BAR")]
	public async Task AcceptsLowerCaseAndTreeNames(string name)
		=> await Assert.That(await IsValidName(name)).IsTrue();

	[Test]
	[MethodDataSource(nameof(PennNameCharacters))]
	public async Task ThePatternSplitterAcceptsEveryValidNameExceptSlash(string name)
	{
		if (name.Contains('/')) return;
		await Assert.That(HelperFunctions.SplitDbRefAndOptionalAttr($"me/{name}"))
			.IsEqualTo(new ObjectWithOptionalAttribute("me", name))
			.Because($"'{name}' is a valid attribute name, so obj/{name} must reach the attribute it names");
	}

	[Test]
	[Arguments("FOO*")]
	[Arguments("F?O")]
	[Arguments("**")]
	[Arguments("^FOO[0-9]$")]
	[Arguments("(A)")]
	public async Task ThePatternSplitterAcceptsWildcards(string pattern)
		=> await Assert.That(HelperFunctions.SplitDbRefAndOptionalAttr($"me/{pattern}"))
			.IsEqualTo(new ObjectWithOptionalAttribute("me", pattern));
}
