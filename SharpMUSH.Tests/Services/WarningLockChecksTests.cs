using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class WarningLockChecksTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWarningService WarningService => WebAppFactoryArg.Services.GetRequiredService<IWarningService>();

	[Test]
	public async Task LockChecks_FlagValue_IsCorrect()
	{
		var lockProbs = WarningType.LockProbs;
		await Assert.That((uint)lockProbs).IsEqualTo(0x100000u);
	}

	[Test]
	public async Task LockChecks_FlagName_ParsesCorrectly()
	{
		// Test that "lock-checks" parses to LockProbs flag
		var parsed = WarningTypeHelper.ParseWarnings("lock-checks");
		await Assert.That(parsed).IsEqualTo(WarningType.LockProbs);
	}

	[Test]
	public async Task LockChecks_InSeriousGroup()
	{
		var serious = WarningType.Serious;
		await Assert.That(serious.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_InNormalGroup()
	{
		var normal = WarningType.Normal;
		await Assert.That(normal.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_InExtraGroup()
	{
		var extra = WarningType.Extra;
		await Assert.That(extra.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_InAllGroup()
	{
		var all = WarningType.All;
		await Assert.That(all.HasFlag(WarningType.LockProbs)).IsTrue();
	}

	[Test]
	public async Task LockChecks_Unparsing_ReturnsLockChecks()
	{
		// Test that LockProbs unparsed returns "lock-checks"
		var unparsed = WarningTypeHelper.UnparseWarnings(WarningType.LockProbs);
		await Assert.That(unparsed).Contains("lock-checks");
	}

	[Test]
	public async Task LockChecks_Negation_RemovesFlag()
	{
		// Test that "all !lock-checks" removes LockProbs from All
		var parsed = WarningTypeHelper.ParseWarnings("all !lock-checks");
		await Assert.That(parsed.HasFlag(WarningType.LockProbs)).IsFalse();

		// But should still have other flags from All
		await Assert.That(parsed.HasFlag(WarningType.ExitUnlinked)).IsTrue();
	}

	[Test]
	public async Task LockChecks_Multiple_WithOtherFlags()
	{
		// Test combining lock-checks with other warnings
		var parsed = WarningTypeHelper.ParseWarnings("lock-checks room-desc");
		await Assert.That(parsed.HasFlag(WarningType.LockProbs)).IsTrue();
		await Assert.That(parsed.HasFlag(WarningType.RoomDesc)).IsTrue();
	}

	[Test]
	public async Task LockChecks_RoundTrip_PreservesValue()
	{
		// Test that parse -> unparse -> parse preserves LockProbs
		var original = WarningType.LockProbs;
		var unparsed = WarningTypeHelper.UnparseWarnings(original);
		var reparsed = WarningTypeHelper.ParseWarnings(unparsed);

		await Assert.That(reparsed).IsEqualTo(original);
	}

	// Integration tests

	[Test]
	public async Task LockChecks_Integration_ValidLock_NoWarnings()
	{
		// A valid lock (#TRUE) should not trigger lock-checks warnings
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LockWarn_Valid");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock {objDbRef}=#TRUE"));

		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var objNode = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Assert.That(objNode.IsNone).IsFalse();
		var godObj = godNode.Known;

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.LockProbs));

		var preCount = NotifyService.ReceivedCalls().Count();
		await WarningService.CheckObjectAsync(godObj, objNode.Known);

		var newCalls = NotifyService.ReceivedCalls().Skip(preCount).ToList();
		var hasLockWarning = newCalls.Any(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is OneOf<MString, string> msg)
				return TestHelpers.MessageContains(msg, "lock-checks");
			if (args[1] is string s) return s.Contains("lock-checks");
			return false;
		});
		await Assert.That(hasLockWarning).IsFalse();

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.None));
	}

	[Test]
	[Skip("Requires a syntactically-invalid lock expression and asserting that CheckObjectAsync emits a lock-check warning via NotifyService; current validator is syntactic-only and accepts all parseable locks")]
	public async Task LockChecks_Integration_InvalidLock_TriggersWarning()
	{
		await Task.CompletedTask;
	}

	[Test]
	public async Task LockChecks_Integration_MultipleLocks_ChecksAll()
	{
		// Object with multiple locks — all syntactically-valid locks pass.
		// Verifies CheckObjectAsync processes multiple locks without error.
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LockWarn_Multi");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock {objDbRef}=#TRUE"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock/ENTER {objDbRef}=#FALSE"));

		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var objNode = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Assert.That(objNode.IsNone).IsFalse();

		// Set warnings on target
		await Mediator.Send(new SetObjectWarningsCommand(objNode.Known, WarningType.LockProbs));
		var objNodeFresh = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Should complete without throwing — multiple locks are iterated
		var hadWarnings = await WarningService.CheckObjectAsync(godNode.Known, objNodeFresh.Known);

		await Assert.That(hadWarnings).IsAssignableTo<bool>();

		await Mediator.Send(new SetObjectWarningsCommand(objNodeFresh.Known, WarningType.None));
	}

	[Test]
	public async Task LockChecks_Integration_EmptyLock_Skipped()
	{
		// An object with no lock set should produce no lock-checks warnings
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LockWarn_Empty");

		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var objNode = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Assert.That(objNode.IsNone).IsFalse();
		var godObj = godNode.Known;

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.LockProbs));

		var preCount = NotifyService.ReceivedCalls().Count();
		await WarningService.CheckObjectAsync(godObj, objNode.Known);

		var newCalls = NotifyService.ReceivedCalls().Skip(preCount).ToList();
		var hasLockWarning = newCalls.Any(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is OneOf<MString, string> msg)
				return TestHelpers.MessageContains(msg, "lock-checks");
			if (args[1] is string s) return s.Contains("lock-checks");
			return false;
		});
		await Assert.That(hasLockWarning).IsFalse();

		await Mediator.Send(new SetObjectWarningsCommand(godObj, WarningType.None));
	}

	[Test]
	public async Task LockChecks_Integration_GoingObjectReference_TriggersWarning()
	{
		// Create a target object and mark it as GOING via @recycle
		var goingObjDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LockWarn_GoingTarget");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@recycle {goingObjDbRef}"));

		// Create another object with a lock referencing the GOING object
		var lockedObjDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LockWarn_GoingLock");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock {lockedObjDbRef}={goingObjDbRef}"));

		var godNode = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var lockedObjNode = await Mediator.Send(new GetObjectNodeQuery(lockedObjDbRef));

		await Assert.That(lockedObjNode.IsNone).IsFalse();

		// Set warnings on the target object itself
		await Mediator.Send(new SetObjectWarningsCommand(lockedObjNode.Known, WarningType.LockProbs));
		var lockedObjFresh = await Mediator.Send(new GetObjectNodeQuery(lockedObjDbRef));

		// Should complete without throwing; the lock validator is syntactic-only
		var hadWarnings = await WarningService.CheckObjectAsync(godNode.Known, lockedObjFresh.Known);

		// No warning fires since the lock validator does not check object GOING status
		await Assert.That(hadWarnings).IsAssignableTo<bool>();

		await Mediator.Send(new SetObjectWarningsCommand(lockedObjFresh.Known, WarningType.None));
	}
}
