using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Every attribute must have an owner — the leaf and every branch parent auto-created along the
/// path. The SurrealDB write path owned only the leaf, so branch parents came back owner-less;
/// <c>examine</c> (which annotates each attribute with its owner) then hit a missing owner and
/// dropped every attribute after the first owner-less one. On a bundled handler object (e.g. #8,
/// whose FN/PM branches were owner-less) it listed a single attribute. This exercises the whole
/// path through <c>examine</c>; the direct write assertion lives in
/// <c>SurrealAttributeEnumerationTests</c>.
/// </summary>
public class ExamineNullOwnerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask Examine_RendersAllAttributes_EvenWhenABranchParentHasNoOwner()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamNullOwner");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LEAFA {objDbRef}=leafa"));
		// Setting only the child auto-creates BRANCHY as a parent with no owner of its own.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BRANCHY`CHILD {objDbRef}=child"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LEAFZ {objDbRef}=leafz"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {objDbRef}"));

		// Every top-level attribute must render — including the owner-less BRANCHY parent and LEAFZ
		// (which, depending on enumeration order, may follow it). Before the fix, dereferencing the
		// null owner threw NullReferenceException and aborted the listing partway.
		foreach (var expected in new[] { "LEAFA", "BRANCHY", "LEAFZ" })
		{
			await NotifyService.Received().Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessageContains(m, expected)),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
		}
	}
}
