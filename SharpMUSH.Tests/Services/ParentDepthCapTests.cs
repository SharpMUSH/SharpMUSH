using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH's <c>do_parent</c> (<c>src/set.c:1432-1446</c>) caps how deep an object's resulting
/// <c>@parent</c> chain can grow: attaching under a prospective parent that already has
/// <c>Limit.MaxParents</c> ancestors above it (10 in the test configuration - see
/// <c>TestSharpMushOptions.cs</c>/<c>OptionsService.cs</c>) is refused with "Too many ancestors.",
/// distinct from the self-reference/cycle rejection <see cref="ZoneParentCycleTests"/> covers.
/// </summary>
/// <remarks>
/// <see cref="AtMaxParents_IsAllowed"/> is the fail-first control: without it, a test asserting only
/// the refusal would keep passing even if <c>@parent</c> were broken outright (e.g. always denying),
/// not just when the depth cap specifically fires.
/// </remarks>
public class ParentDepthCapTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IManipulateSharpObjectService ManipulateService => WebAppFactoryArg.Services.GetRequiredService<IManipulateSharpObjectService>();

	private async ValueTask<AnySharpObject> CreateAsync(string name)
	{
		var result = await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {name}"));
		var dbref = DBRef.Parse(result.Message!.ToPlainText()!);
		var node = await Mediator.Send(new GetObjectNodeQuery(dbref));
		return node.Known;
	}

	/// <summary>
	/// Builds a chain of <paramref name="ancestorCount"/> objects above a fresh head object -
	/// head's parent is Anc0, Anc0's parent is Anc1, ..., and the topmost ancestor has no parent -
	/// by sending <see cref="SetObjectParentCommand"/> directly, bypassing <c>SafeToAddParent</c>
	/// exactly as <see cref="ZoneParentCycleTests"/> does. The point here is to construct a
	/// pre-existing chain, not to exercise the guard under test.
	/// </summary>
	private async ValueTask<AnySharpObject> BuildAncestorChainAsync(string label, int ancestorCount)
	{
		var head = await CreateAsync($"{label}Head");
		var current = head;
		for (var i = 0; i < ancestorCount; i++)
		{
			var next = await CreateAsync($"{label}Anc{i}");
			await Mediator.Send(new SetObjectParentCommand(current, next));
			current = next;
		}

		return head;
	}

	[Test]
	public async ValueTask AtMaxParents_IsAllowed()
	{
		// 9 ancestors above the prospective parent => attaching a child gives the child exactly
		// 10 total ancestors (the prospective parent + the 9 above it), landing exactly at
		// Limit.MaxParents. This must succeed.
		var prospectiveParent = await BuildAncestorChainAsync("AtLimit", 9);
		var child = await CreateAsync("AtLimitChild");

		var result = await ManipulateService.SetParent(child, child, prospectiveParent, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).IsNotEqualTo(ErrorMessages.Returns.TooManyAncestors);
		await Assert.That(message).IsNotEqualTo(ErrorMessages.Returns.ParentLoop);

		var updated = await Mediator.Send(new GetObjectNodeQuery(child.Object().DBRef));
		var parent = await updated.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parent.IsNone).IsFalse();
		await Assert.That(parent.Known.Object().DBRef.Number).IsEqualTo(prospectiveParent.Object().DBRef.Number);
	}

	[Test]
	public async ValueTask OneOverMaxParents_IsRefused()
	{
		// 10 ancestors above the prospective parent => attaching a child would give it 11 total
		// ancestors, one past Limit.MaxParents. This must be refused, with a message distinct from
		// the loop/self-reference rejection.
		var prospectiveParent = await BuildAncestorChainAsync("OverLimit", 10);
		var child = await CreateAsync("OverLimitChild");

		var result = await ManipulateService.SetParent(child, child, prospectiveParent, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).IsEqualTo(ErrorMessages.Returns.TooManyAncestors);
		await Assert.That(message).Contains("ANCESTORS");

		var updated = await Mediator.Send(new GetObjectNodeQuery(child.Object().DBRef));
		var parent = await updated.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parent.IsNone).IsTrue();
	}
}
