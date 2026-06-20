using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Settles the claim raised while writing SceneRoleplayIntegrationTests — that "loc()/@tel need the
/// full objid (#N:creation); the bare #N form does not work". It does NOT. The engine resolves an
/// object by EITHER form (<see cref="Library.Models.DBRef"/>.Matches resolves a bare dbref on number
/// alone; the providers only reject a full objid whose timestamp mismatches), and @tel moves an
/// object by either form.
///
/// The agent's real symptom was a separate, narrow caching bug: <c>loc()</c> read by a BARE #N returns
/// a STALE location after a move, because <c>GetObjectNodeQuery.CacheKey</c> keys on the
/// timestamp-sensitive <c>DBRef</c> (so a bare-#N read caches under <c>object:#N</c>, which the move's
/// invalidation — keyed by the object's full objid — never clears). The move itself is correct; only
/// <c>loc()</c>-by-bare is stale. That case is captured (skipped) below.
/// </summary>
[NotInParallel]
public class DbrefBothFormsResolutionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<string> Eval(string expression)
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"think {expression}"));
		return result.Message?.ToPlainText()?.Trim() ?? "";
	}

	private async ValueTask Cmd(string command)
		=> await Parser.CommandParse(1, ConnectionService, MModule.single(command));

	private async ValueTask<string> CmdOut(string command)
		=> (await Parser.CommandParse(1, ConnectionService, MModule.single(command))).Message?.ToPlainText()?.Trim() ?? "";

	/// <summary>The bare "#N" of a "#N" or "#N:creation" dbref string.</summary>
	private static string Short(string dbref) => dbref.Contains(':') ? dbref[..dbref.IndexOf(':')] : dbref;

	[Test]
	public async ValueTask Player_NameAndLoc_ResolveByBothShortAndFullDbref()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var name = $"DualRef_{tag}";

		await Cmd("@set #1=WIZARD");
		await Cmd($"@pcreate {name}=pw_{tag}");

		var resolved = await Eval($"pmatch({name})");
		await Assert.That(resolved).IsNotEqualTo("#-1").Because("pmatch should resolve the new player");

		var shortForm = Short(resolved);                  // #N
		var fullForm = await Eval($"objid({shortForm})");  // #N:creation
		await Assert.That(fullForm).Contains(":").Because("objid() should return the full #N:creation form");
		await Assert.That(Short(fullForm)).IsEqualTo(shortForm);

		// name() — both forms resolve the player.
		await Assert.That(await Eval($"name({shortForm})")).IsEqualTo(name)
			.Because("name() must resolve a player by the bare #N form");
		await Assert.That(await Eval($"name({fullForm})")).IsEqualTo(name)
			.Because("name() must resolve a player by the full #N:creation objid");

		// loc() — both forms resolve, to the same place.
		await Assert.That(await Eval($"loc({shortForm})")).IsNotEqualTo("#-1")
			.Because("loc() must accept the bare #N form for a player");
		await Assert.That(Short(await Eval($"loc({shortForm})"))).IsEqualTo(Short(await Eval($"loc({fullForm})")))
			.Because("loc() must return the same location for the short and full forms");
	}

	[Test]
	public async ValueTask Tel_MovesAnObject_ByBothShortAndFullDbref()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];

		// Park in a known room so each fresh thing lands in our inventory (local) when we @tel it.
		await Cmd("@tel me=#0");
		var roomA = Short(await CmdOut($"@dig DualRoomA_{tag}"));
		var roomB = Short(await CmdOut($"@dig DualRoomB_{tag}"));

		var t1 = Short((await CmdOut($"@create DualThingS_{tag}")));   // moved by bare #N
		var t2Full = await CmdOut($"@create DualThingF_{tag}");        // moved by full objid
		var t2 = Short(t2Full);

		await Cmd($"@tel {t1}={roomA}");
		await Cmd($"@tel {t2Full}={roomB}");

		// Verify the moves via the room contents (lcon is read fresh — not subject to the bare-#N
		// loc() cache staleness captured in the skipped test below).
		await Assert.That(await Eval($"lcon({roomA})")).Contains(t1)
			.Because("@tel must move an object referenced by the bare #N form (verified via room contents)");
		await Assert.That(await Eval($"lcon({roomB})")).Contains(t2)
			.Because("@tel must move an object referenced by the full objid form");
	}

	[Test]
	public async ValueTask Loc_ByBareDbref_IsFreshAfterMove()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		await Cmd("@tel me=#0");
		var room = Short(await CmdOut($"@dig StaleLocRoom_{tag}"));
		var thingFull = await CmdOut($"@create StaleLocThing_{tag}");
		var thing = Short(thingFull);

		// Prime the bare-#N loc cache, then move the thing.
		_ = await Eval($"loc({thing})");
		await Cmd($"@tel {thing}={room}");

		// loc() by the bare #N should reflect the move (currently returns the stale pre-move location).
		await Assert.That(Short(await Eval($"loc({thing})"))).IsEqualTo(room)
			.Because("loc() by the bare #N must reflect a move (full objid loc() and lcon() already do)");
	}

	// The recycle/objid guard the cache fix must NOT regress: a full objid with a mismatched creation
	// time must never resolve the live #N — even after the bare #N (number-keyed) entry is cached.
	[Test]
	public async ValueTask ObjidWithWrongTimestamp_DoesNotResolve_EvenWhenBareIsCached()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var name = $"ObjidThing_{tag}";
		var thingFull = await CmdOut($"@create {name}");
		var thing = Short(thingFull);                       // #N
		var objid = await Eval($"objid({thing})");          // #N:realts

		// Prime the by-number cache with the bare form, then confirm the correct objid still resolves.
		await Assert.That(await Eval($"name({thing})")).IsEqualTo(name)
			.Because("the bare #N resolves the object");
		await Assert.That(await Eval($"name({objid})")).IsEqualTo(name)
			.Because("the correct full objid resolves the object");

		// A WRONG-timestamp objid must NOT resolve the live #N, despite object:#N being cached.
		await Assert.That(await Eval($"name({thing}:1)")).IsNotEqualTo(name)
			.Because("a full objid with a mismatched creation time must not resolve the live #N (recycle validation must run on every request)");
	}
}
