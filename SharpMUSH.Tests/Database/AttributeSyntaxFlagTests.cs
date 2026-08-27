using DotNext.Threading;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// <c>SetAttributeFlagAsync</c> falls back to PennMUSH-style shortest-prefix matching (e.g. <c>wiz</c>
/// resolves to <c>wizard</c>), so <c>@set obj/attr=wiz</c> works. <c>UnsetAttributeFlagAsync</c> used to
/// be exact-match only, so the symmetric <c>@set obj/attr=!wiz</c> failed. The tests below cover the fix.
/// <para>
/// <see cref="WebAppFactoryArg"/> is <c>SharedType.PerTestSession</c>, so <see cref="NotifyService"/> is
/// one substitute shared across the whole process. Tests read notifications via
/// <see cref="MessagesWhile"/>, which tracks the recipient-keyed recorder rather than clearing or
/// enumerating the substitute's shared call list, so they stay correct under parallel execution.
/// </para>
/// </summary>
[NotInParallel]
public class AttributeSyntaxFlagTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	/// <summary>
	/// Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran, in
	/// order. The <see cref="INotifyService"/> substitute is shared across the whole test session,
	/// so this reads the recipient-keyed recorder rather than enumerating (or clearing)
	/// <c>ReceivedCalls()</c> while parallelizable tests are still recording into it.
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private static bool HasFlag(SharpAttribute attribute, string name)
		=> attribute.Flags.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

	[Test]
	public async ValueTask UnsetAttributeFlag_BangWizPrefix_UnsetsWizard()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UnsetFlagWiz");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&UNSETWIZ_ATTR {objDbRef}=hello"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {objDbRef}/UNSETWIZ_ATTR=wizard"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var beforeAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "UNSETWIZ_ATTR",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(HasFlag(beforeAttr.AsAttribute.Last(), "wizard")).IsTrue()
			.Because("precondition: wizard must be set before we can test unsetting it via prefix");

		// "wiz" is a prefix of "wizard", not an exact name or symbol match -- this is exactly the
		// asymmetric case that used to fail on the unset path while `@set .../attr=wiz` succeeded.
		var messages = await MessagesWhile(executor, () =>
			Parser.CommandParse(1, ConnectionService,
				MModule.single($"@set {objDbRef}/UNSETWIZ_ATTR=!wiz")).AsTask());

		await Assert.That(messages.Any(m => m.EndsWith("/UNSETWIZ_ATTR - wizard reset.")))
			.IsTrue()
			.Because("the prefix `!wiz` must resolve to wizard and report the unset by name");

		var afterAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "UNSETWIZ_ATTR",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(HasFlag(afterAttr.AsAttribute.Last(), "wizard")).IsFalse();
	}

	[Test]
	public async ValueTask UnsetAttributeFlag_BangXSymbol_UnsetsCmdSyntax()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "UnsetFlagCmdX");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&UNSETX_ATTR {objDbRef}=$hi:@pemit %#=hi"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {objDbRef}/UNSETX_ATTR=cmdsyntax"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var beforeAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "UNSETX_ATTR",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(HasFlag(beforeAttr.AsAttribute.Last(), "cmdsyntax")).IsTrue()
			.Because("precondition: cmdsyntax must be set before we can test unsetting it via its symbol");

		var messages = await MessagesWhile(executor, () =>
			Parser.CommandParse(1, ConnectionService,
				MModule.single($"@set {objDbRef}/UNSETX_ATTR=!x")).AsTask());

		await Assert.That(messages.Any(m => m.EndsWith("/UNSETX_ATTR - cmdsyntax reset.")))
			.IsTrue()
			.Because("the symbol `!x` must resolve to cmdsyntax and report the unset by name");

		var afterAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "UNSETX_ATTR",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(HasFlag(afterAttr.AsAttribute.Last(), "cmdsyntax")).IsFalse();
	}

	private static string SymbolFor(string name) => name switch
	{
		"cmdsyntax" => "x",
		"funsyntax" => "f",
		_ => string.Empty
	};

	private static SharpAttribute WithFlags(params string[] names) => new(
		Id: "attribute/1",
		Key: "TEST",
		Name: "TEST",
		Flags: names.Select(n => new SharpAttributeFlag
		{
			Name = n, Symbol = SymbolFor(n), System = true, Inheritable = true
		}).ToArray(),
		CommandListIndex: null,
		LongName: "TEST",
		Leaves: new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
		Owner: new AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(null)),
		SharpAttributeEntry: new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult<SharpAttributeEntry?>(null)))
	{
		Value = MModule.single("say hi")
	};

	[Test]
	public async Task CmdSyntaxFlag_MapsToCommandList()
	{
		await Assert.That(WithFlags("cmdsyntax").IsCmdSyntax()).IsTrue();
		await Assert.That(WithFlags("cmdsyntax").SyntaxParseType()).IsEqualTo(ParseType.CommandList);
	}

	[Test]
	public async Task FunSyntaxFlag_MapsToFunction()
	{
		await Assert.That(WithFlags("funsyntax").IsFunSyntax()).IsTrue();
		await Assert.That(WithFlags("funsyntax").SyntaxParseType()).IsEqualTo(ParseType.Function);
	}

	[Test]
	public async Task BothFlags_CommandWins()
		=> await Assert.That(WithFlags("cmdsyntax", "funsyntax").SyntaxParseType())
			.IsEqualTo(ParseType.CommandList);

	[Test]
	public async Task NoFlags_ReturnsNull()
		=> await Assert.That(WithFlags().SyntaxParseType()).IsNull();

	[Test]
	public async Task IsNoDebug_MatchesSeededFlagName()
		=> await Assert.That(WithFlags("no_debug").IsNoDebug()).IsTrue();
}
