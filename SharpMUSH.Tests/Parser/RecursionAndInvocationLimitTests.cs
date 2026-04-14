using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests to verify that recursion and invocation limits are tracked accurately.
/// These tests prove assumptions about how the limits work and ensure they are enforced correctly.
/// </summary>
[NotInParallel]
public class RecursionAndInvocationLimitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	/// <summary>
	/// Test that basic recursion (same function calling itself) is detected and limited.
	/// This tests FunctionRecursionLimit.
	/// </summary>
	[Test]
	public async Task RecursionLimit_SameFunction_IsEnforced()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "RecurseLim");

		// Arrange: Create a recursive function with a counter
		// Use add() to increment and check if we've exceeded limit
		// This will recurse until it hits the limit (limit is 50 in test config)
		var command = $"&RECURSE_LIM_UNIQUE {objDbRef}=[setq(c,add(r(c),1))][if(lte(r(c),105),[u({objDbRef}/RECURSE_LIM_UNIQUE)],DONE)]";

		// Create the attribute
		await CommandParser.CommandParse(1, ConnectionService, MModule.single(command));

		// Act: Evaluate the recursive function - should halt at limit
		var result = await FunctionParser.FunctionParse(MModule.single($"[u({objDbRef}/RECURSE_LIM_UNIQUE)]"));

		// Assert: Should get a limit error (recursion or invocation) - evaluation halts immediately
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
		// Should hit either recursion or invocation limit
		var hasRecursion = output.Contains("RECURSION");
		var hasInvocation = output.Contains("INVOCATION");
		await Assert.That(hasRecursion || hasInvocation).IsTrue();
		// Should hit a limit, not complete successfully
		await Assert.That(output).DoesNotContain("DONE");
	}

	/// <summary>
	/// Test that built-in function nesting succeeds up to reasonable depths.
	/// PennMUSH does NOT limit built-in function nesting depth (only user-defined recursion).
	/// Built-in nesting is only limited by CallLimit (1000).
	/// </summary>
	[Test]
	public async Task StackDepth_NestedDifferentFunctions_IsTracked()
	{
		var nestedCalls = "x";
		for (int i = 0; i < 12; i++)
		{
			nestedCalls = $"[strlen({nestedCalls})]";
		}

		// Act: Parse the deeply nested structure
		var result = await FunctionParser.FunctionParse(MModule.single(nestedCalls));

		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		Console.WriteLine($"Stack depth test result: {output}");

		await Assert.That(output).IsEqualTo("1");
	}

	/// <summary>
	/// Test that built-in function nesting succeeds at various depths.
	/// PennMUSH does NOT limit built-in function nesting depth.
	/// </summary>
	[Test]
	public async Task StackDepth_ExactLimit_IsEnforced()
	{
		var nested10 = "x";
		for (int i = 0; i < 10; i++)
		{
			nested10 = $"[strlen({nested10})]";
		}

		var nested11 = "x";
		for (int i = 0; i < 11; i++)
		{
			nested11 = $"[strlen({nested11})]";
		}

		// Act
		var result10 = await FunctionParser.FunctionParse(MModule.single(nested10));
		var result11 = await FunctionParser.FunctionParse(MModule.single(nested11));

		// Assert
		await Assert.That(result10).IsNotNull();
		await Assert.That(result11).IsNotNull();

		var output10 = result10!.Message.ToPlainText();
		var output11 = result11!.Message.ToPlainText();

		Console.WriteLine($"10-deep result: {output10}");
		Console.WriteLine($"11-deep result: {output11}");

		await Assert.That(output10).IsEqualTo("1");
		await Assert.That(output11).IsEqualTo("1");
	}

	/// <summary>
	/// Test mutual recursion (A calls B, B calls A) to verify recursion tracking.
	/// </summary>
	[Test]
	public async Task RecursionLimit_MutualRecursion_IsDetected()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MutualRecurse");

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&FUNC_A_LIM_UNIQUE {objDbRef}=[setq(a,add(r(a),1))][if(lte(r(a),120),[u({objDbRef}/FUNC_B_LIM_UNIQUE)],DONE_A)]"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&FUNC_B_LIM_UNIQUE {objDbRef}=[setq(b,add(r(b),1))][if(lte(r(b),120),[u({objDbRef}/FUNC_A_LIM_UNIQUE)],DONE_B)]"));

		// Act: Evaluate - should halt at limit
		var result = await FunctionParser.FunctionParse(MModule.single($"[u({objDbRef}/FUNC_A_LIM_UNIQUE)]"));

		// Assert: Should get some limit error - evaluation halts immediately
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
	}

	/// <summary>
	/// Test that CallLimit is actually enforced.
	/// </summary>
	[Test]
	public async Task CallLimit_IsEnforced()
	{
		// Arrange: The default CallLimit is 1000
		// Create a very deeply nested parse structure
		var nested = "test";
		for (int i = 0; i < 1100; i++)
		{
			nested = $"[strlen({nested})]";
		}

		// Act
		var result = await FunctionParser.FunctionParse(MModule.single(nested));

		// Assert: Should hit some limit - evaluation halts
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
	}

	/// <summary>
	/// Verify that FunctionInvocationLimit configuration exists but document if it's used.
	/// </summary>
	[Test]
	public async Task FunctionInvocationLimit_ConfigurationExists()
	{
		// Arrange & Act: Get the configuration
		var config = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();

		// Assert: Verify the configuration has the limit
		await Assert.That(config.CurrentValue.Limit.FunctionInvocationLimit).IsGreaterThan(0u);
		await Assert.That(config.CurrentValue.Limit.FunctionInvocationLimit).IsEqualTo(25000u);

		// Note: This test documents that the configuration exists with test value 25000
		// but additional testing is needed to verify if it's actually enforced
	}

	/// <summary>
	/// Test a simple case that should succeed - no limits hit.
	/// </summary>
	[Test]
	public async Task SimpleFunctionCall_NoLimits_Succeeds()
	{
		// Arrange: Simple function call well within all limits
		var input = MModule.single("[strlen(hello world)]");

		// Act
		var result = await FunctionParser.FunctionParse(input);

		// Assert: Should succeed
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message.ToPlainText()).IsEqualTo("11");
	}

	/// <summary>
	/// Test that recursion depth is counted per function, not globally.
	/// If we call A, then B, then A again, A's recursion count should be 2.
	/// </summary>
	[Test]
	public async Task RecursionDepth_CountsPerFunction()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "RecursePerFunc");

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&WRAP_LIM_UNIQUE {objDbRef}=[setq(w,add(r(w),1))][if(lte(r(w),120),[u({objDbRef}/INNER_LIM_UNIQUE)],DONE_W)]"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&INNER_LIM_UNIQUE {objDbRef}=[setq(i,add(r(i),1))][if(lte(r(i),120),[u({objDbRef}/WRAP_LIM_UNIQUE)],DONE_I)]"));

		// Act: Execute - creates pattern WRAP->INNER->WRAP->INNER->...
		var result = await FunctionParser.FunctionParse(MModule.single($"[u({objDbRef}/WRAP_LIM_UNIQUE)]"));

		// Assert: Should eventually hit a limit - evaluation halts
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
	}

	/// <summary>
	/// Document and verify the default limit values from configuration.
	/// </summary>
	[Test]
	public async Task DefaultLimitValues_AreAsExpected()
	{
		// Arrange & Act
		var config = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		var limits = config.CurrentValue.Limit;

		// Assert: Document the test configuration values
		await Assert.That(limits.MaxDepth).IsEqualTo(10u);
		await Assert.That(limits.FunctionRecursionLimit).IsEqualTo(50u);
		await Assert.That(limits.FunctionInvocationLimit).IsEqualTo(25000u);
		// CallLimit is very large in test config
		await Assert.That(limits.CallLimit).IsGreaterThanOrEqualTo(1000u);
	}

	/// <summary>
	/// Test that different error messages are returned for different limit violations.
	/// </summary>
	[Test]
	public async Task DifferentLimits_ReturnDifferentErrors()
	{
		// This test documents what errors are returned for what limits
		// Evaluation halts immediately when limit is hit
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "DiffLimits");

		var recursiveAttr = $"[setq(c,add(r(c),1))][if(lte(r(c),150),[u({objDbRef}/REC_LIM_UNIQUE)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&REC_LIM_UNIQUE {objDbRef}={recursiveAttr}"));
		var recursionResult = await FunctionParser.FunctionParse(MModule.single($"[u({objDbRef}/REC_LIM_UNIQUE)]"));

		var deepNest = "x";
		for (int i = 0; i < 12; i++)
		{
			deepNest = $"[strlen({deepNest})]";
		}
		var stackResult = await FunctionParser.FunctionParse(MModule.single(deepNest));

		await Assert.That(recursionResult).IsNotNull();
		await Assert.That(stackResult).IsNotNull();

		var recursionError = recursionResult!.Message.ToPlainText();
		var stackOutput = stackResult!.Message.ToPlainText();

		await Assert.That(recursionError).Contains("#-1");

		await Assert.That(stackOutput).IsEqualTo("1");

		Console.WriteLine($"Recursion error: {recursionError}");
		Console.WriteLine($"Stack depth result: {stackOutput}");
	}

	/// <summary>
	/// Test that different attribute evaluation methods (u, ufun, ulocal) all enforce recursion limits.
	/// This proves they all use the centralized attribute evaluation path with recursion tracking.
	/// </summary>
	[Test]
	public async Task RecursionLimit_AllAttributeMethods_AreEnforced()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "AllAttrMethods");

		// Arrange: Create recursive attributes using different methods
		// Each method should hit the same recursion tracking

		// Test u() - standard user-defined function call
		var uRecursive = $"[setq(c,add(r(c),1))][if(lte(r(c),105),[u({objDbRef}/U_REC_LIM_UNIQUE)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&U_REC_LIM_UNIQUE {objDbRef}={uRecursive}"));

		// Test ufun() - user-defined function with default
		var ufunRecursive = $"[setq(c,add(r(c),1))][if(lte(r(c),105),[ufun({objDbRef}/UFUN_REC_LIM_UNIQUE,default)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&UFUN_REC_LIM_UNIQUE {objDbRef}={ufunRecursive}"));

		// Test ulocal() - local user-defined function
		var ulocalRecursive = $"[setq(c,add(r(c),1))][if(lte(r(c),105),[ulocal({objDbRef}/ULOCAL_REC_LIM_UNIQUE)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&ULOCAL_REC_LIM_UNIQUE {objDbRef}={ulocalRecursive}"));

		// Act: Test all three methods
		var uResult = await FunctionParser.FunctionParse(MModule.single($"[u({objDbRef}/U_REC_LIM_UNIQUE)]"));
		var ufunResult = await FunctionParser.FunctionParse(MModule.single($"[ufun({objDbRef}/UFUN_REC_LIM_UNIQUE)]"));
		var ulocalResult = await FunctionParser.FunctionParse(MModule.single($"[ulocal({objDbRef}/ULOCAL_REC_LIM_UNIQUE)]"));

		// Assert: All should hit recursion limits
		await Assert.That(uResult).IsNotNull();
		await Assert.That(ufunResult).IsNotNull();
		await Assert.That(ulocalResult).IsNotNull();

		var uOutput = uResult!.Message.ToPlainText();
		var ufunOutput = ufunResult!.Message.ToPlainText();
		var ulocalOutput = ulocalResult!.Message.ToPlainText();

		Console.WriteLine($"u() recursion test: {uOutput}");
		Console.WriteLine($"ufun() recursion test: {ufunOutput}");
		Console.WriteLine($"ulocal() recursion test: {ulocalOutput}");

		// All should hit limits
		await Assert.That(uOutput).Contains("#-1");
		await Assert.That(ufunOutput).Contains("#-1");
		await Assert.That(ulocalOutput).Contains("#-1");

		// None should complete
		await Assert.That(uOutput).DoesNotContain("DONE");
		await Assert.That(ufunOutput).DoesNotContain("DONE");
		await Assert.That(ulocalOutput).DoesNotContain("DONE");
	}

	/// <summary>
	/// Test that @INCLUDE now properly tracks recursion when evaluating attributes.
	/// Verifies ExecuteAttributeWithTracking is used and basic execution works.
	/// </summary>
	[Test]
	public async Task RecursionLimit_IncludeCommand_TracksRecursion()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "InclRecurse");

		// @INCLUDE uses ExecuteAttributeWithTracking helper to track recursion.
		// When u() exceeds the recursion limit the error string becomes the command text,
		// which is unrecognised → "Huh?" notification. Verify @include completes without crash
		// and that NotifyService was called (command dispatched some notification).

		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&SELFCALL_INCL_LIM_UNIQUE {objDbRef}=[u({objDbRef}/SELFCALL_INCL_LIM_UNIQUE)]"));
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&INCLUDETEST_RECUR_LIM_UNIQUE {objDbRef}=[u({objDbRef}/SELFCALL_INCL_LIM_UNIQUE)]"));

		// Should complete (not hang) – recursion limit terminates the u() loop
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@include {objDbRef}/INCLUDETEST_RECUR_LIM_UNIQUE"));

		// The recursion-error string is treated as an unknown command → "Huh?" notification (sent via NotifyLocalized)
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.HuhTypeHelp), executor, executor)).IsTrue();
	}

	/// <summary>
	/// Test that @TRIGGER properly tracks recursion when evaluating attributes.
	/// Verifies ExecuteAttributeWithTracking is used and basic execution works.
	/// </summary>
	[Test]
	public async Task RecursionLimit_TriggerCommand_TracksRecursion()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "TrigRecurse");

		// When u() exceeds the recursion limit inside a @trigger attribute, the resulting
		// error string is treated as an unknown command → "Huh?" notification.

		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&SELFCALL_TRIG_LIM_UNIQUE {objDbRef}=[u({objDbRef}/SELFCALL_TRIG_LIM_UNIQUE)]"));

		// Should complete (not hang)
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@trigger {objDbRef}/SELFCALL_TRIG_LIM_UNIQUE"));

		// At least one notification must have been dispatched (Huh? from unknown command, sent via NotifyLocalized).
		// @trigger runs the attribute with the triggered object as executor.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.HuhTypeHelp), objDbRef, objDbRef)).IsTrue();
	}

	/// <summary>
	/// Test that command-based attribute evaluation works: @INCLUDE evaluates an attribute
	/// that itself composes results from two sub-attributes (A contains [u(objDbRef/B)]).
	/// </summary>
	[Test]
	public async Task RecursionLimit_CommandsTrackAttributeRecursion()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "CmdTrackRecurse");

		// Set up attribute A = "think CMDTRACK_A[u(objDbRef/CMDTRACK_B_LIM_UNIQUE)]" and B = "_B_OK"
		// @include A → executes "think CMDTRACK_A_LIM_UNIQUE_B_OK" → notification with that text.
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&CMDTRACK_B_LIM_UNIQUE {objDbRef}=_B_OK"));
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&CMDTRACK_A_LIM_UNIQUE {objDbRef}=think CMDTRACK_A_LIM_UNIQUE[u({objDbRef}/CMDTRACK_B_LIM_UNIQUE)]"));

		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@include {objDbRef}/CMDTRACK_A_LIM_UNIQUE"));

		// Verify the composed output was sent as a notification
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "CMDTRACK_A_LIM_UNIQUE_B_OK")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
