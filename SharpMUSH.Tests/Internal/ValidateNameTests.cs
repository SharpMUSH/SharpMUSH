using Mediator;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// Object-name validation (<see cref="IValidateService.ValidationType.Name"/>).
///
/// The rule the character class has always described is: no leading or trailing space, and none of
/// <c>[ ] % \ = &amp; |</c> anywhere. The pattern did not enforce it — it carried a <c>$</c> but no
/// <c>^</c>, and its middle term matched the forbidden set rather than its complement, so
/// <see cref="Regex.IsMatch"/> could satisfy it against the last character or two of any string.
/// Every forbidden character passed as long as it was not at the very end.
///
/// Names reach this only on create/rename and through the <c>valid()</c> function, so tightening it
/// cannot reject an object that already exists — it stops new ones being made.
///
/// <c>;</c> stays legal: <c>@open</c> splits exit aliases on it (<c>@open north;n</c>).
/// </summary>
public class ValidateNameTests
{
	private static ValidateService Service() => new(
		Substitute.For<IMediator>(),
		Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
		Substitute.For<ILockService>());

	private static async ValueTask<bool> IsValid(string name)
		=> await Service().Valid(IValidateService.ValidationType.Name, MModule.single(name), new None());

	[Test]
	[Arguments("TestName")]
	[Arguments("X")]
	[Arguments("a red ball")]
	[Arguments("Bob's Hat")]
	[Arguments("north;n")]
	[Arguments("A Room (Upstairs)")]
	[Arguments("widget-42_v2")]
	public async Task AcceptsOrdinaryNames(string name)
		=> await Assert.That(await IsValid(name)).IsTrue().Because($"'{name}' is a legal object name");

	[Test]
	[Arguments("foo[bar")]
	[Arguments("foo]bar")]
	[Arguments("foo%bar")]
	[Arguments("foo\\bar")]
	[Arguments("foo=bar")]
	[Arguments("foo&bar")]
	[Arguments("foo|bar")]
	public async Task RejectsAForbiddenCharacterInTheMiddle(string name)
		=> await Assert.That(await IsValid(name)).IsFalse()
			.Because($"'{name}' contains a forbidden character; the old pattern matched only the tail");

	[Test]
	[Arguments("[leading")]
	[Arguments("%leading")]
	[Arguments("=leading")]
	public async Task RejectsAForbiddenCharacterAtTheStart(string name)
		=> await Assert.That(await IsValid(name)).IsFalse().Because($"'{name}' starts with a forbidden character");

	[Test]
	[Arguments("trailing[")]
	[Arguments("trailing%")]
	[Arguments("trailing|")]
	public async Task RejectsAForbiddenCharacterAtTheEnd(string name)
		=> await Assert.That(await IsValid(name)).IsFalse().Because($"'{name}' ends with a forbidden character");

	[Test]
	[Arguments(" leading space")]
	[Arguments("trailing space ")]
	[Arguments("  ")]
	[Arguments(" ")]
	public async Task RejectsLeadingAndTrailingWhitespace(string name)
		=> await Assert.That(await IsValid(name)).IsFalse().Because($"'{name}' is space-padded");

	/// <summary>
	/// Control characters are not names. A tab or an embedded newline would corrupt every
	/// line-oriented surface the name appears on, and <c>$</c> in .NET matches immediately before a
	/// trailing <c>\n</c> — so an anchor of <c>$</c> rather than <c>\z</c> accepts <c>"name\n"</c>.
	/// </summary>
	[Test]
	[Arguments("na\tme")]
	[Arguments("na\nme")]
	[Arguments("name\n")]
	[Arguments("name\r\n")]
	[Arguments("\nname")]
	[Arguments("\tname")]
	public async Task RejectsControlCharacters(string name)
		=> await Assert.That(await IsValid(name)).IsFalse()
			.Because($"'{name.Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r")}' contains a control character");

	[Test]
	public async Task RejectsAnEmptyName()
		=> await Assert.That(await IsValid(string.Empty)).IsFalse();

	[Test]
	[Arguments("me")]
	[Arguments("here")]
	[Arguments("home")]
	[Arguments("!")]
	public async Task RejectsMagicCookies(string name)
		=> await Assert.That(await IsValid(name)).IsFalse().Because($"'{name}' is a lookup token, not a name");

	[Test]
	public async Task RejectsNonAsciiNames()
		=> await Assert.That(await IsValid("Café")).IsFalse();
}
