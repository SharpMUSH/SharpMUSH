using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Every attribute must have an owner — the leaf and every branch parent auto-created along the
/// path. <c>examine</c> annotates each attribute with its owner, so an owner-less branch parent
/// would make it drop every attribute after the first one; on a bundled handler object (e.g. #8,
/// with its FN/PM branches) it would list a single attribute. This exercises the whole path
/// through <c>examine</c>.
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

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&LEAFA {objDbRef}=leafa"));
		// Setting only the child auto-creates BRANCHY as a parent with no owner of its own.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&BRANCHY`CHILD {objDbRef}=child"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&LEAFZ {objDbRef}=leafz"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"examine {objDbRef}"));

		// Every top-level attribute must render — including the owner-less BRANCHY parent and LEAFZ
		// (which, depending on enumeration order, may follow it). Before the fix, dereferencing the
		// null owner threw NullReferenceException and aborted the listing partway.
		foreach (var expected in new[] { "LEAFA", "BRANCHY", "LEAFZ" })
		{
			await NotifyService.Received().Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<SharpMessage>(m => TestHelpers.MessageContains(m, expected)),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
		}
	}
}
