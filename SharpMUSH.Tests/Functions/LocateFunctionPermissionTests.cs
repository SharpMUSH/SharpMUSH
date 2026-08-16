using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>fun_locate</c>'s relative-scope gate (fundb.c): when the requested scopes depend on where the
/// looker stands, the <em>executor</em> must be able to evaluate against that looker — near it,
/// controlling it, or See_All — or the whole call answers <c>#-1</c>.
///
/// <para>The gate and the match have different permission subjects. <c>match_result(looker, …)</c>
/// asks its can_interact/controls questions about the <b>looker</b>, while the gate above it asks
/// about the <b>executor</b>. Collapsing the two turns the gate into <c>nearby(looker, looker)</c>,
/// which is always true, and a mortal can then search any object's surroundings by naming it.</para>
/// </summary>
public class LocateFunctionPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<string> EvalAs(DBRef executor, string expr)
		=> (await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MModule.single(expr)))
			?.Message!.ToPlainText() ?? "<null>";

	private async Task<string> God(string command)
		=> (await GodParser.CommandParse(1, ConnectionService, MModule.single(command)))?.Message?.ToPlainText() ?? "";

	[Test]
	[NotInParallel]
	public async Task AMortalCannotSearchARemoteLookersNeighbours()
	{
		// A room the mortal is nowhere near, holding a looker and something for it to find. The looker
		// is a thing rather than the room itself: MAT_NEIGHBOR searches Contents(loc) and match.c skips
		// that scope when loc == where, which is always so for a room (match.c:437).
		var room = DBRef.Parse((await God("@dig LocatePermRoom")).Trim().Split(' ')[^1].Trim());
		var looker = DBRef.Parse((await God("@create LocatePermLooker")).Trim());
		var target = DBRef.Parse((await God("@create LocatePermTarget")).Trim());
		await God($"@tel #{looker.Number}=#{room.Number}");
		await God($"@tel #{target.Number}=#{room.Number}");

		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LocatePerm");

		// 'n' is MAT_NEIGHBOR — a looker-relative scope, so the gate applies. The mortal is neither
		// near the looker, nor controls it, nor See_All.
		var mortalSees = await EvalAs(mortal.DbRef, $"locate(#{looker.Number},LocatePermTarget,n)");

		// God is See_All, so the same call passes the gate and finds it — proving the refusal above is
		// the gate talking and not simply a search that was never going to match.
		var godSees = await EvalAs(new DBRef(1), $"locate(#{looker.Number},LocatePermTarget,n)");

		await Assert.That(mortalSees).IsEqualTo("#-1");
		await Assert.That(godSees).IsEqualTo($"#{target.Number}");
	}

	[Test]
	[NotInParallel]
	public async Task TheGateSurvivesDefaultScopeInjection()
	{
		// fun_locate injects the default scope set *before* it gates (fundb.c), so a flags string that
		// names no scope at all still ends up searching the looker's surroundings — and still has to
		// clear the gate. 'N' is NOTYPE: not a scope, so the injection fires and MAT_NEIGHBOR and
		// friends arrive implicitly. Gating on the flags as typed reads no relative-scope bit and waves
		// the call through.
		var room = DBRef.Parse((await God("@dig LocateInjectRoom")).Trim().Split(' ')[^1].Trim());
		var looker = DBRef.Parse((await God("@create LocateInjectLooker")).Trim());
		var target = DBRef.Parse((await God("@create LocateInjectTarget")).Trim());
		await God($"@tel #{looker.Number}=#{room.Number}");
		await God($"@tel #{target.Number}=#{room.Number}");

		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LocateInject");

		var mortalSees = await EvalAs(mortal.DbRef, $"locate(#{looker.Number},LocateInjectTarget,N)");
		var godSees = await EvalAs(new DBRef(1), $"locate(#{looker.Number},LocateInjectTarget,N)");

		await Assert.That(mortalSees).IsEqualTo("#-1");
		await Assert.That(godSees).IsEqualTo($"#{target.Number}");
	}
}
