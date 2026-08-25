using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The read side of the <c>@parent</c> chain has no write-time guard to rely on: legacy data, a
/// direct database edit, or a bug in some other write path could leave a cycle in place even
/// though <c>SafeToAddParent</c>/<c>ExceedsMaxParentDepthAsync</c> keep every in-app write from
/// creating one. <see cref="GetAttributesQueryHandler.GetAttributesWithParentsAsync"/> (backing
/// <c>IAttributeService.GetAttributePatternAsync</c> with <c>checkParents: true</c> - the path
/// <c>lattr()</c>-style callers use) used to walk that chain with an unconditional <c>while
/// (true)</c>, so a cycle there hung the read forever. <see cref="GetCommandAttributesQueryHandler"/>
/// and <see cref="GetAncestorCommandAttributesQueryHandler"/> had the identical unbounded walk for
/// $-command inheritance.
/// </summary>
/// <remarks>
/// The cycle is built by sending <see cref="SetObjectParentCommand"/> directly in both directions -
/// the same way <c>ZoneParentCycleTests.cs</c> bypasses <c>SafeToAddParent</c> to set up its
/// fixtures - since going through <c>ManipulateSharpObjectService.SetParent</c> would (correctly)
/// refuse to ever create the cycle in the first place. Each test carries a
/// <see cref="TimeoutAttribute"/> so a regression here fails the test outright instead of hanging
/// the whole suite.
/// </remarks>
public class AttributeReadParentCycleTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private async ValueTask<AnySharpObject> CreateAsync(string name)
	{
		var result = await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {name}"));
		var dbref = DBRef.Parse(result.Message!.ToPlainText()!);
		var node = await Mediator.Send(new GetObjectNodeQuery(dbref));
		return node.Known;
	}

	private async ValueTask<(AnySharpObject A, AnySharpObject B)> BuildDirectParentCycleAsync(string label)
	{
		var a = await CreateAsync($"{label}A");
		var b = await CreateAsync($"{label}B");

		// Bypasses SafeToAddParent on both edges - this is the only way to get a genuine cycle
		// into the database, since the guarded path (ManipulateSharpObjectService.SetParent)
		// refuses the second edge.
		await Mediator.Send(new SetObjectParentCommand(a, b));
		await Mediator.Send(new SetObjectParentCommand(b, a));

		return (a, b);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask GetAttributePatternAsync_WithParentCycle_Terminates(CancellationToken ct)
	{
		var (a, _) = await BuildDirectParentCycleAsync("AttrCycle");
		await AttributeService.SetAttributeAsync(a, a, "CYCLETEST", MModule.single("hello"));

		var result = await AttributeService.GetAttributePatternAsync(
			a, a, "*", checkParents: true, IAttributeService.AttributePatternMode.Wildcard);

		await Assert.That(result.IsError).IsFalse();
		await Assert.That(result.AsAttributes.Any(attr => attr.LongName == "CYCLETEST")).IsTrue();
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask GetCommandAttributesQuery_WithParentCycle_Terminates(CancellationToken ct)
	{
		var (a, _) = await BuildDirectParentCycleAsync("CmdCycle");

		var result = await Mediator.Send(new GetCommandAttributesQuery(a), ct);

		await Assert.That(result).IsNotNull();
	}
}
