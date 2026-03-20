using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for CommandLock enforcement and @pcreate EqSplit fix.
/// </summary>
[NotInParallel]
public class CommandLockTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask PcreateCommandCreatesPlayer()
	{
		var uniqueName = TestIsolationHelpers.GenerateUniqueName("PcreateTest");
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@pcreate {uniqueName}=TestPassword123"));

		// The result should contain a valid DBRef
		var resultText = result.Message!.ToPlainText()!;
		var newDb = DBRef.Parse(resultText);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.IsNone).IsFalse();
		await Assert.That(newObject.Known.Object().Name).IsEqualTo(uniqueName);
	}

	[Test]
	public async ValueTask CommandLockBlocksNonWizard()
	{
		// Create a non-wizard player
		var nonWizardDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NonWizCmdLock");

		// Create a parser context that runs as this non-wizard player
		var nonWizParser = Parser.Push(Parser.CurrentState with { Executor = nonWizardDbRef });

		// Try to use @dump (which has CommandLock = "FLAG^WIZARD")
		var result = await nonWizParser.CommandParse(MModule.single("@dump"));
		var resultText = result.Message?.ToPlainText() ?? "";

		// Should be denied
		await Assert.That(resultText).Contains("PERMISSION DENIED");
	}

	[Test]
	public async ValueTask CommandLockAllowsWizard()
	{
		// Player #1 (God) should be able to use wizard commands
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@dump"));
		var resultText = result.Message?.ToPlainText() ?? "";

		// Should NOT return permission denied
		await Assert.That(resultText).DoesNotContain("PERMISSION DENIED");
	}

	[Test]
	public async ValueTask PcreateCommandLockBlocksNonWizard()
	{
		// Create a non-wizard player
		var nonWizardDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NonWizPcreate");

		// Create a parser context that runs as this non-wizard player
		var nonWizParser = Parser.Push(Parser.CurrentState with { Executor = nonWizardDbRef });

		// Try to use @pcreate (which has CommandLock = "FLAG^WIZARD")
		var uniqueName = TestIsolationHelpers.GenerateUniqueName("ShouldNotCreate");
		var result = await nonWizParser.CommandParse(MModule.single($"@pcreate {uniqueName}=TestPassword123"));
		var resultText = result.Message?.ToPlainText() ?? "";

		// Should be denied
		await Assert.That(resultText).Contains("PERMISSION DENIED");
	}
}
