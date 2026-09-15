using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Pins the object-flag seed against PennMUSH for the two flags that share the letter <c>m</c>, and
/// against itself for the invariant that a flag name is seeded exactly once.
///
/// <para>PennMUSH has two flags on <c>m</c>, told apart by object type: MISTRUST
/// (<c>src/flags.c:778</c> — <c>TYPE_THING | TYPE_EXIT | TYPE_ROOM</c>) and MYOPIC
/// (<c>game/txt/hlp/pennflag.hlp:333</c> — players). <c>pennflag.hlp:37</c> lists the shared letter as
/// "m - Mistrust/Myopic", and that line is the likely source of the error this fixes: all three
/// SharpMUSH providers seeded MYOPIC as an <em>alias</em> of MISTRUST, so the two flags could not be
/// set independently and MISTRUST carried the wrong type list into the bargain.</para>
///
/// <para>A shared symbol is normal here and needs no special handling —
/// <c>SharpObjectFlag.Symbol</c> documents itself as not unique, and the seed already ships
/// ABODE/ANSI on 'A' and NO_LEAVE/NO_TEL on 'N', each pair separated by its type restrictions.
/// <see cref="NoTwoFlagsShareALetterOnOneObjectType"/> holds that line to its word.</para>
/// </summary>
public class FlagSeedIntegrityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task MistrustIsSeededWithPennMushTypesAndNoMyopicAlias()
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery("MISTRUST"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("MISTRUST");
		await Assert.That(flag.Symbol).IsEqualTo("m");
		await Assert.That(flag.Aliases ?? [])
			.DoesNotContain("MYOPIC")
			.Because("MYOPIC is its own flag in PennMUSH, not another name for MISTRUST");
		await Assert.That(flag.TypeRestrictions.Order(StringComparer.Ordinal).ToArray())
			.IsEquivalentTo(new[] { "EXIT", "ROOM", "THING" })
			.Because("src/flags.c:778 declares MISTRUST as TYPE_THING | TYPE_EXIT | TYPE_ROOM");
	}

	/// <summary>
	/// <c>hdrs/flag_tab.h:25</c> declares STICKY as <c>'S'</c> on <c>NOTYPE</c> — every object type.
	/// It backs a room's drop-to (<c>src/move.c:272</c>) and <c>safe_tel</c>'s stripping of carried
	/// objects (<c>src/move.c:311</c>), neither of which can fire while the flag is unseeded.
	/// </summary>
	[Test]
	public async Task StickyIsSeededForEveryObjectType()
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery("STICKY"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("STICKY");
		await Assert.That(flag.Symbol).IsEqualTo("S");
		await Assert.That(flag.TypeRestrictions.Order(StringComparer.Ordinal).ToArray())
			.IsEquivalentTo(new[] { "EXIT", "PLAYER", "ROOM", "THING" });
	}

	[Test]
	public async Task MyopicIsSeededAsItsOwnPlayerFlag()
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery("MYOPIC"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name)
			.IsEqualTo("MYOPIC")
			.Because("asking for MYOPIC must not resolve to MISTRUST");
		await Assert.That(flag.Symbol).IsEqualTo("m");
		await Assert.That(flag.TypeRestrictions)
			.IsEquivalentTo(new[] { "PLAYER" })
			.Because("pennflag.hlp:333 documents MYOPIC as a player flag");
	}

	/// <summary>
	/// The four flags whose seeded type list or letter did not match PennMUSH's own: CHOWN_OK is not a
	/// player flag there and is an exit flag, PUPPET reaches rooms, Z_TEL reaches things, and GOING is
	/// <c>G</c> so that GAGGED can keep <c>g</c>.
	///
	/// <para>The first two are not in <c>hdrs/flag_tab.h</c>, which declares CHOWN_OK as
	/// <c>NOTYPE</c> and PUPPET as <c>TYPE_THING</c>; <c>flag_add_additional</c> narrows the one and
	/// widens the other at startup (<c>src/flags.c:792</c> and <c>:796</c>), and the table PennMUSH
	/// dumps is the one after that runs — TestData/pennmush-1.8.8p0.outdb, the oracle's own, agrees
	/// line for line. Z_TEL is <c>TYPE_THING | TYPE_ROOM</c> in <c>flag_tab.h:67</c> and GOING
	/// <c>'G'</c> against GAGGED's <c>'g'</c> in <c>:19</c> and <c>:50</c>.</para>
	///
	/// <para>The type list is what gates the flag, so a narrow one is not cosmetic: the PennMUSH
	/// importer refuses a flag its own table does not allow on that object type, which silently
	/// dropped CHOWN_OK from every imported exit, PUPPET from every room and Z_TEL from every
	/// thing.</para>
	/// </summary>
	[Test]
	[Arguments("CHOWN_OK", "C", new[] { "EXIT", "ROOM", "THING" })]
	[Arguments("PUPPET", "p", new[] { "ROOM", "THING" })]
	[Arguments("Z_TEL", "Z", new[] { "ROOM", "THING" })]
	[Arguments("GOING", "G", new[] { "EXIT", "PLAYER", "ROOM", "THING" })]
	public async Task TheseFlagsAreSeededWithPennMushLettersAndTypes(string name, string symbol, string[] types)
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery(name));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Symbol).IsEqualTo(symbol);
		await Assert.That(flag.TypeRestrictions.Order(StringComparer.Ordinal).ToArray()).IsEquivalentTo(types);
	}

	/// <summary>
	/// A letter is how <c>@set</c> output and the flag lists name a flag, and PennMUSH allows one
	/// meaning per object type (<c>letter_to_flagptr</c>, src/flags.c). A shared letter is fine and
	/// this seed relies on it — ABODE/ANSI on 'A', MISTRUST/MYOPIC on 'm' — but only where the two
	/// flags can never meet on one object. Two that can makes the letter ambiguous on that type.
	/// </summary>
	[Test]
	public async Task NoTwoFlagsShareALetterOnOneObjectType()
	{
		var flags = await Mediator.CreateStream(new GetAllObjectFlagsQuery()).ToListAsync();

		var lettered = flags.Where(flag => !string.IsNullOrEmpty(flag.Symbol)).ToArray();
		await Assert.That(lettered).IsNotEmpty().Because("no letters would make the assertion below vacuous");

		var ambiguous = (
			from left in lettered
			from right in lettered
			where string.CompareOrdinal(left.Name, right.Name) < 0 && left.Symbol == right.Symbol
			let shared = Shared(left.TypeRestrictions, right.TypeRestrictions)
			where shared.Length > 0
			select $"{left.Name}/{right.Name} both '{left.Symbol}' on {string.Join(" ", shared)}")
			.Order(StringComparer.Ordinal)
			.ToArray();

		await Assert.That(ambiguous).IsEmpty()
			.Because("a letter may mean two things only when no object can carry both flags");
	}

	/// <summary>The object types both flags allow; an empty restriction list means every type.</summary>
	private static string[] Shared(string[] left, string[] right)
	{
		string[] every = ["EXIT", "PLAYER", "ROOM", "THING"];
		return
		[
			.. (left.Length == 0 ? every : left)
				.Intersect(right.Length == 0 ? every : right, StringComparer.OrdinalIgnoreCase)
				.Order(StringComparer.Ordinal)
		];
	}

	/// <summary>
	/// A flag name is its persistent identity, so duplicates make lookups order-dependent.
	/// </summary>
	[Test]
	public async Task NoFlagNameIsSeededTwice()
	{
		var duplicates = await Mediator.CreateStream(new GetAllObjectFlagsQuery())
			.ToListAsync();

		var offenders = duplicates
			.GroupBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.Key} x{group.Count()}")
			.ToArray();

		await Assert.That(offenders)
			.IsEmpty()
			.Because("a flag name is its identity; two rows sharing one makes every lookup order-dependent");
	}

	/// <summary>
	/// An attribute entry may only name
	/// flags that actually exist. <c>SetAttributeAsync</c> resolves each <c>DefaultFlags</c> name to
	/// a flag row and silently skips the ones it cannot find, so a name with no row is not an error
	/// anywhere - the attribute is simply created without the flag its own entry asked for, on that
	/// provider only.
	/// </summary>
	[Test]
	public async Task EveryDefaultFlagNamedByAnAttributeEntryExists()
	{
		var flagNames = await Mediator.CreateStream(new GetAttributeFlagsQuery())
			.Select(f => f.Name)
			.ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

		var entries = await Mediator.CreateStream(new GetAllAttributeEntriesQuery()).ToArrayAsync();

		await Assert.That(entries).IsNotEmpty()
			.Because("an empty entry table would make the assertion below vacuous");

		var dangling = entries
			.SelectMany(e => (e.DefaultFlags ?? []).Select(f => $"{e.Name} -> {f}"))
			.Where(pair => !flagNames.Contains(pair.Split(" -> ")[1]))
			.Order(StringComparer.Ordinal)
			.ToArray();

		await Assert.That(dangling).IsEmpty()
			.Because("every flag named in an attribute entry's DefaultFlags must exist in the attribute-flag table");
	}

	/// <summary>
	/// The specific instance the test above generalises.
	/// </summary>
	[Test]
	public async Task PrefixMatchAttributeFlagIsSeeded()
	{
		var flag = await Mediator.CreateStream(new GetAttributeFlagsQuery())
			.FirstOrDefaultAsync(f => f.Name.Equals("prefixmatch", StringComparison.OrdinalIgnoreCase));

		await Assert.That(flag).IsNotNull()
			.Because("127 standard attribute entries name prefixmatch in their DefaultFlags");
		await Assert.That(flag!.Symbol ?? string.Empty).IsEqualTo(string.Empty)
			.Because("PennMUSH has no character for this flag");
	}
}
