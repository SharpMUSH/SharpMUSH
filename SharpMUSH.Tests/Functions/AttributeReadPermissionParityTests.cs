using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>get()</c>, <c>xget()</c> and <c>u()</c> on another player's attributes, as a mortal who
/// cannot examine them (GRA-145).
/// <para>
/// PennMUSH's <c>fetch_ufun_attrib</c> (<c>src/utils.c:244-253</c>) asks <c>Can_Read_Attr</c>
/// (<c>#-1 NO PERMISSION TO GET ATTRIBUTE</c>) before <c>CanEvalAttr</c> (<c>#-1 PERMISSION DENIED</c>);
/// <c>u()</c> used to ask only the second, and so ran attributes the caller could not <c>get()</c>.
/// <c>do_get_attrib</c> (<c>src/fundb.c:55-73</c>) answers an unset attribute empty only if a standard
/// attribute of that name would be readable, or, for any other name, if the caller can examine the
/// object. Expected strings were taken from PennMUSH through <c>tools/parity</c> (case <c>perm.ufun</c>).
/// </para>
/// </summary>
public class AttributeReadPermissionParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task Cmd(long handle, string command)
		=> await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));

	/// <summary>Evaluates <paramref name="expression"/> as the player behind <paramref name="handle"/>.</summary>
	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think {expression}"));
		return result?.Message.ToPlainText() ?? string.Empty;
	}

	private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpper();

	private async Task<long> MortalHandle(string name)
		=> (await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, name)).Handle;

	[Test]
	public async ValueTask UnreadableAttribute_IsNeitherReadNorEvaluated()
	{
		var uid = Uid();
		var viewer = await MortalHandle("PermUfunH");
		await Cmd(1, $"&PERMH{uid} #1=h[add(1,1)]");

		await Assert.That(await Eval(viewer, $"[get(#1/PERMH{uid})]|[xget(#1,PERMH{uid})]|[u(#1/PERMH{uid})]"))
			.IsEqualTo($"{ErrorMessages.Returns.AttrPermissions}|{ErrorMessages.Returns.AttrPermissions}|{ErrorMessages.Returns.AttrPermissions}");
	}

	[Test]
	public async ValueTask VisualAttributeOnWizard_IsReadableButNotEvaluable()
	{
		var uid = Uid();
		var viewer = await MortalHandle("PermUfunV");
		await Cmd(1, $"&PERMV{uid} #1=v[add(1,1)]");
		await Cmd(1, $"@set #1/PERMV{uid}=visual");

		await Assert.That(await Eval(viewer, $"[get(#1/PERMV{uid})]|[u(#1/PERMV{uid})]"))
			.IsEqualTo($"v[add(1,1)]|{ErrorMessages.Returns.PermissionDenied}");
	}

	[Test]
	public async ValueTask VisualAttributeOnMortal_IsReadAndEvaluated()
	{
		var uid = Uid();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermUfunO");
		var viewer = await MortalHandle("PermUfunB");
		await Cmd(owner.Handle, $"&PERMB{uid} me=b[add(1,1)]");
		await Cmd(owner.Handle, $"@set me/PERMB{uid}=visual");

		await Assert.That(await Eval(viewer, $"[get({owner.DbRef}/PERMB{uid})]|[u({owner.DbRef}/PERMB{uid})]"))
			.IsEqualTo("b[add(1,1)]|b2");
	}

	[Test]
	public async ValueTask UnsetAttribute_OnUnexaminableObject_IsNoPermissionForGetButEmptyForU()
	{
		var uid = Uid();
		var viewer = await MortalHandle("PermUfunN");

		await Assert.That(await Eval(viewer, $"[get(#1/NOPE{uid})]|[xget(#1,NOPE{uid})]|[u(#1/NOPE{uid})]"))
			.IsEqualTo($"{ErrorMessages.Returns.AttrPermissions}|{ErrorMessages.Returns.AttrPermissions}|");
	}

	[Test]
	public async ValueTask UnsetAttribute_IsEmptyOnlyWhereItWouldBeReadable()
	{
		var uid = Uid();
		var viewer = await MortalHandle("PermUfunS");

		// SUCCESS is a standard attribute whose unset read is judged like a set one; NOPE is not, so
		// only examining the object decides it.
		await Assert.That(await Eval(viewer, $"[get(#1/SUCCESS)]|[get(me/NOPE{uid})]|[get(me/SUCCESS)]"))
			.IsEqualTo($"{ErrorMessages.Returns.AttrPermissions}||");
	}

	[Test]
	public async ValueTask UnsetAttribute_ByStandardAlias_IsJudgedByTheAliasedEntry()
	{
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermUfunA");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PermUfunBox");
		await Cmd(1, $"@tel {thing}={viewer.DbRef}");

		// atr_match resolves DESC to DESCRIBE, whose entry is visual and nearby: a carried object's unset
		// description reads empty under either name. IDESCRIBE's entry is not visual.
		await Assert.That(await Eval(viewer.Handle, $"[get({thing}/DESCRIBE)]|[get({thing}/DESC)]|[get({thing}/IDESC)]"))
			.IsEqualTo($"||{ErrorMessages.Returns.AttrPermissions}");
	}
}
