using DotNext.Threading;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Pins every predicate in <c>SharpAttributeExtensions</c> to a flag name that is actually seeded,
/// against PennMUSH's <c>attr_privs_set</c> (<c>src/atr_tab.c:34-60</c>).
///
/// <para>These are pure predicates over an in-memory <see cref="SharpAttribute"/>, so no database
/// is needed and none of them can be flaky. That matters here: a predicate that names a flag no
/// provider seeds fails <em>silently</em> - it returns false forever, and the only symptom is a gate
/// that never fires. Five of them were in that state.</para>
/// </summary>
public class AttributePredicateNameTests
{
	private static SharpAttribute With(string value, params string[] flagNames) => new(
		Id: "attribute/1",
		Key: "TEST",
		Name: "TEST",
		Flags: flagNames.Select(n => new SharpAttributeFlag
		{
			Name = n, Symbol = string.Empty, System = true, Inheritable = true
		}).ToArray(),
		CommandListIndex: null,
		LongName: "TEST",
		Leaves: new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
		Owner: new AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(null)),
		SharpAttributeEntry: new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult<SharpAttributeEntry?>(null)))
	{
		Value = MModule.single(value)
	};

	private static SharpAttribute WithFlags(params string[] flagNames) => With("say hi", flagNames);

	/// <summary>AF_NOPROG is spelled <c>no_command</c> in the flag table; "noprog" is the C symbol.</summary>
	[Test]
	public async Task IsNoprog_ReadsTheNoCommandFlag()
	{
		await Assert.That(WithFlags("no_command").IsNoprog()).IsTrue();
		await Assert.That(WithFlags("noprog").IsNoprog()).IsFalse()
			.Because("\"noprog\" is PennMUSH's C symbol for the bit, never a stored flag name");
	}

	/// <summary>
	/// AF_PRIVATE carries two names on <c>'i'</c> — <c>no_inherit</c> and <c>private</c>. SharpMUSH
	/// seeds only the first, so <c>IsPrivate</c> must resolve to it.
	/// </summary>
	[Test]
	public async Task IsPrivate_IsTheSamePredicateAsIsNoInherit()
	{
		await Assert.That(WithFlags("no_inherit").IsPrivate()).IsTrue();
		await Assert.That(WithFlags("no_inherit").IsPrivate())
			.IsEqualTo(WithFlags("no_inherit").IsNoInherit())
			.Because("attr_privs_set gives one bit both names (atr_tab.c:35-36)");
	}

	[Test]
	public async Task IsNoDebug_ReadsTheSeededUnderscoreName()
	{
		await Assert.That(WithFlags("no_debug").IsNoDebug()).IsTrue();
		await Assert.That(WithFlags("nodebug").IsNoDebug()).IsFalse();
	}

	[Test]
	public async Task HearPredicates_ReadTheSeededAmhearAndAahearNames()
	{
		await Assert.That(WithFlags("amhear").IsMortalHear()).IsTrue();
		await Assert.That(WithFlags("aahear").IsActionHear()).IsTrue();
		await Assert.That(WithFlags("mortalhear").IsMortalHear()).IsFalse();
		await Assert.That(WithFlags("actionhear").IsActionHear()).IsFalse();
		await Assert.That(WithFlags("aahear").IsMortalHear()).IsFalse()
			.Because("AF_AHEAR and AF_MHEAR are different bits, not synonyms");
	}

	/// <summary>
	/// <c>set_cmd_flags</c> (<c>src/attrib.c:840-859</c>) requires the sigil AND an unescaped colon.
	/// </summary>
	[Test]
	public async Task IsCommand_RequiresAnUnescapedColonNotJustTheSigil()
	{
		await Assert.That(With("$hello:say hi").IsCommand()).IsTrue();
		await Assert.That(With("$hello").IsCommand()).IsFalse()
			.Because("the sigil alone is not a command in Penn - set_cmd_flags scans for the colon");
		await Assert.That(With(@"$hello\:there").IsCommand()).IsFalse()
			.Because("an escaped colon does not terminate the pattern");
		await Assert.That(With(@"$hello\\:say hi").IsCommand()).IsTrue()
			.Because("an escaped backslash leaves the following colon as a real terminator");
		await Assert.That(With("$hello:say hi", "no_command").IsCommand()).IsFalse()
			.Because("no_command suppresses it, matching CommandAttributeScanner's own gate");
	}

	/// <summary>
	/// AF_LISTEN is INTERNAL in Penn (<c>hdrs/attrib.h:152</c>) and derived by the same switch as
	/// AF_COMMAND, so there is no flag to read.
	/// </summary>
	[Test]
	public async Task IsListen_IsDerivedFromTheCaretPatternNotAStoredFlag()
	{
		await Assert.That(With("^hello:say hi").IsListen()).IsTrue();
		await Assert.That(With("^hello").IsListen()).IsFalse();
		await Assert.That(With("$hello:say hi").IsListen()).IsFalse()
			.Because("a $-command is not a listen pattern");
		await Assert.That(WithFlags("listen").IsListen()).IsFalse()
			.Because("no provider seeds a \"listen\" flag and Penn has no name for the bit");
	}

	/// <summary>
	/// Stored casing is not guaranteed canonical (imported data, hand-edited records), and every
	/// gate in this file used a case-sensitive comparison.
	/// </summary>
	[Test]
	public async Task PredicatesAreCaseInsensitive()
	{
		await Assert.That(WithFlags("WIZARD").IsWizard()).IsTrue();
		await Assert.That(WithFlags("Mortal_Dark").IsMortalDark()).IsTrue();
		await Assert.That(WithFlags("VISUAL").IsVisual()).IsTrue();
		await Assert.That(WithFlags("No_Command").IsNoprog()).IsTrue();
		await Assert.That(WithFlags("AMHEAR").IsMortalHear()).IsTrue();
	}
}
