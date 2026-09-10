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
/// ABODE/ANSI on 'A', CHOWN_OK/COLOR on 'C' and NO_LEAVE/NO_TEL on 'N', each pair separated by its
/// type restrictions.</para>
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
