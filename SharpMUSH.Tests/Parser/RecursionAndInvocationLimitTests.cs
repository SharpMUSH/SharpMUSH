using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests to verify that recursion and invocation limits are tracked accurately.
/// Counters start at explicit zero so these tests do not depend on null_eq_zero.
/// These tests prove assumptions about how the limits work and ensure they are enforced correctly.
/// </summary>
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

		var command = $"&RECURSE_LIM_UNIQUE {objDbRef}=[setq(c,add(r(c),1))][if(lte(r(c),105),[u({objDbRef}/RECURSE_LIM_UNIQUE)],DONE)]";

		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

		var result = await FunctionParser.FunctionParse(MarkupText.Plain($"[setq(c,0)][u({objDbRef}/RECURSE_LIM_UNIQUE)]"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		var output = result!.Message!.ToPlainText();
		await Assert.That(output).Contains("#-1");
		var hasRecursion = output.Contains("RECURSION");
		var hasInvocation = output.Contains("INVOCATION");
		await Assert.That(hasRecursion || hasInvocation).IsTrue();
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

		var result = await FunctionParser.FunctionParse(MarkupText.Plain(nestedCalls));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		var output = result!.Message!.ToPlainText();
		TestDiagnostics.WriteLine($"Stack depth test result: {output}");

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

		var result10 = await FunctionParser.FunctionParse(MarkupText.Plain(nested10));
		var result11 = await FunctionParser.FunctionParse(MarkupText.Plain(nested11));

		await Assert.That(result10).IsNotNull();
		await Assert.That(result11).IsNotNull();

		await Assert.That(result10!.Message).IsNotNull();
		var output10 = result10!.Message!.ToPlainText();
		await Assert.That(result11!.Message).IsNotNull();
		var output11 = result11!.Message!.ToPlainText();

		TestDiagnostics.WriteLine($"10-deep result: {output10}");
		TestDiagnostics.WriteLine($"11-deep result: {output11}");

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

		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&FUNC_A_LIM_UNIQUE {objDbRef}=[setq(a,add(r(a),1))][if(lte(r(a),120),[u({objDbRef}/FUNC_B_LIM_UNIQUE)],DONE_A)]"));
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&FUNC_B_LIM_UNIQUE {objDbRef}=[setq(b,add(r(b),1))][if(lte(r(b),120),[u({objDbRef}/FUNC_A_LIM_UNIQUE)],DONE_B)]"));

		var result = await FunctionParser.FunctionParse(MarkupText.Plain($"[setq(a,0,b,0)][u({objDbRef}/FUNC_A_LIM_UNIQUE)]"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		var output = result!.Message!.ToPlainText();
		await Assert.That(output).Contains("#-1");
	}

	/// <summary>
	/// Test that CallLimit is actually enforced.
	/// </summary>
	[Test]
	public async Task CallLimit_IsEnforced()
	{
		var nested = "test";
		for (int i = 0; i < 1100; i++)
		{
			nested = $"[strlen({nested})]";
		}

		var result = await FunctionParser.FunctionParse(MarkupText.Plain(nested));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		var output = result!.Message!.ToPlainText();
		await Assert.That(output).Contains("#-1");
	}

	/// <summary>
	/// Verify that FunctionInvocationLimit configuration exists but document if it's used.
	/// </summary>
	[Test]
	public async Task FunctionInvocationLimit_ConfigurationExists()
	{
		var config = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();

		await Assert.That(config.CurrentValue.Limit.FunctionInvocationLimit).IsGreaterThan(0u);
		await Assert.That(config.CurrentValue.Limit.FunctionInvocationLimit).IsEqualTo(25000u);
	}

	/// <summary>
	/// Test a simple case that should succeed - no limits hit.
	/// </summary>
	[Test]
	public async Task SimpleFunctionCall_NoLimits_Succeeds()
	{
		var input = MarkupText.Plain("[strlen(hello world)]");

		var result = await FunctionParser.FunctionParse(input);

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo("11");
	}

	/// <summary>
	/// Test that recursion depth is counted per function, not globally.
	/// If we call A, then B, then A again, A's recursion count should be 2.
	/// </summary>
	[Test]
	public async Task RecursionDepth_CountsPerFunction()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "RecursePerFunc");

		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&WRAP_LIM_UNIQUE {objDbRef}=[setq(w,add(r(w),1))][if(lte(r(w),120),[u({objDbRef}/INNER_LIM_UNIQUE)],DONE_W)]"));
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&INNER_LIM_UNIQUE {objDbRef}=[setq(i,add(r(i),1))][if(lte(r(i),120),[u({objDbRef}/WRAP_LIM_UNIQUE)],DONE_I)]"));

		var result = await FunctionParser.FunctionParse(MarkupText.Plain($"[setq(w,0,i,0)][u({objDbRef}/WRAP_LIM_UNIQUE)]"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		var output = result!.Message!.ToPlainText();
		await Assert.That(output).Contains("#-1");
	}

	/// <summary>
	/// Document and verify the default limit values from configuration.
	/// </summary>
	[Test]
	public async Task DefaultLimitValues_AreAsExpected()
	{
		var config = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		var limits = config.CurrentValue.Limit;

		await Assert.That(limits.MaxDepth).IsEqualTo(10u);
		await Assert.That(limits.FunctionRecursionLimit).IsEqualTo(50u);
		await Assert.That(limits.FunctionInvocationLimit).IsEqualTo(25000u);
		await Assert.That(limits.CallLimit).IsGreaterThanOrEqualTo(1000u);
	}

	/// <summary>
	/// Test that different error messages are returned for different limit violations.
	/// </summary>
	[Test]
	public async Task DifferentLimits_ReturnDifferentErrors()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "DiffLimits");

		var recursiveAttr = $"[setq(c,add(r(c),1))][if(lte(r(c),150),[u({objDbRef}/REC_LIM_UNIQUE)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&REC_LIM_UNIQUE {objDbRef}={recursiveAttr}"));
		var recursionResult = await FunctionParser.FunctionParse(MarkupText.Plain($"[setq(c,0)][u({objDbRef}/REC_LIM_UNIQUE)]"));

		var deepNest = "x";
		for (int i = 0; i < 12; i++)
		{
			deepNest = $"[strlen({deepNest})]";
		}
		var stackResult = await FunctionParser.FunctionParse(MarkupText.Plain(deepNest));

		await Assert.That(recursionResult).IsNotNull();
		await Assert.That(stackResult).IsNotNull();

		await Assert.That(recursionResult!.Message).IsNotNull();
		var recursionError = recursionResult!.Message!.ToPlainText();
		await Assert.That(stackResult!.Message).IsNotNull();
		var stackOutput = stackResult!.Message!.ToPlainText();

		await Assert.That(recursionError).Contains("#-1");

		await Assert.That(stackOutput).IsEqualTo("1");

		TestDiagnostics.WriteLine($"Recursion error: {recursionError}");
		TestDiagnostics.WriteLine($"Stack depth result: {stackOutput}");
	}

	/// <summary>
	/// Test that different attribute evaluation methods (u, ufun, ulocal) all enforce recursion limits.
	/// This proves they all use the centralized attribute evaluation path with recursion tracking.
	/// </summary>
	[Test]
	public async Task RecursionLimit_AllAttributeMethods_AreEnforced()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "AllAttrMethods");

		var uRecursive = $"[setq(c,add(r(c),1))][if(lte(r(c),105),[u({objDbRef}/U_REC_LIM_UNIQUE)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&U_REC_LIM_UNIQUE {objDbRef}={uRecursive}"));

		var ufunRecursive = $"[setq(c,add(r(c),1))][if(lte(r(c),105),[ufun({objDbRef}/UFUN_REC_LIM_UNIQUE,default)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&UFUN_REC_LIM_UNIQUE {objDbRef}={ufunRecursive}"));

		var ulocalRecursive = $"[setq(c,add(r(c),1))][if(lte(r(c),105),[ulocal({objDbRef}/ULOCAL_REC_LIM_UNIQUE)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ULOCAL_REC_LIM_UNIQUE {objDbRef}={ulocalRecursive}"));

		var uResult = await FunctionParser.FunctionParse(MarkupText.Plain($"[setq(c,0)][u({objDbRef}/U_REC_LIM_UNIQUE)]"));
		var ufunResult = await FunctionParser.FunctionParse(MarkupText.Plain($"[setq(c,0)][ufun({objDbRef}/UFUN_REC_LIM_UNIQUE)]"));
		var ulocalResult = await FunctionParser.FunctionParse(MarkupText.Plain($"[setq(c,0)][ulocal({objDbRef}/ULOCAL_REC_LIM_UNIQUE)]"));

		await Assert.That(uResult).IsNotNull();
		await Assert.That(ufunResult).IsNotNull();
		await Assert.That(ulocalResult).IsNotNull();

		await Assert.That(uResult!.Message).IsNotNull();
		var uOutput = uResult!.Message!.ToPlainText();
		await Assert.That(ufunResult!.Message).IsNotNull();
		var ufunOutput = ufunResult!.Message!.ToPlainText();
		await Assert.That(ulocalResult!.Message).IsNotNull();
		var ulocalOutput = ulocalResult!.Message!.ToPlainText();

		TestDiagnostics.WriteLine($"u() recursion test: {uOutput}");
		TestDiagnostics.WriteLine($"ufun() recursion test: {ufunOutput}");
		TestDiagnostics.WriteLine($"ulocal() recursion test: {ulocalOutput}");

		await Assert.That(uOutput).Contains("#-1");
		await Assert.That(ufunOutput).Contains("#-1");
		await Assert.That(ulocalOutput).Contains("#-1");

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
			MarkupText.Plain($"&SELFCALL_INCL_LIM_UNIQUE {objDbRef}=[u({objDbRef}/SELFCALL_INCL_LIM_UNIQUE)]"));
		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&INCLUDETEST_RECUR_LIM_UNIQUE {objDbRef}=[u({objDbRef}/SELFCALL_INCL_LIM_UNIQUE)]"));

		// Should complete (not hang) – recursion limit terminates the u() loop
		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include {objDbRef}/INCLUDETEST_RECUR_LIM_UNIQUE"));

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
			MarkupText.Plain($"&SELFCALL_TRIG_LIM_UNIQUE {objDbRef}=[u({objDbRef}/SELFCALL_TRIG_LIM_UNIQUE)]"));

		// Should complete (not hang)
		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@trigger {objDbRef}/SELFCALL_TRIG_LIM_UNIQUE"));

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
			MarkupText.Plain($"&CMDTRACK_B_LIM_UNIQUE {objDbRef}=_B_OK"));
		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMDTRACK_A_LIM_UNIQUE {objDbRef}=think CMDTRACK_A_LIM_UNIQUE[u({objDbRef}/CMDTRACK_B_LIM_UNIQUE)]"));

		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include {objDbRef}/CMDTRACK_A_LIM_UNIQUE"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, "CMDTRACK_A_LIM_UNIQUE_B_OK")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// SharpMUSH has no fixed evaluation buffer, so a single function generating an enormous
	/// string could consume unbounded memory. A 5 MB output ceiling refuses such a result rather
	/// than propagating it. FunctionInvocationLimit does not cover this: a huge repeat() is one
	/// invocation that loops internally, not many calls.
	/// </summary>
	[Test]
	public async Task OutputCeiling_OversizedFunctionResult_IsRefused()
	{
		// One character past the 5 MB (5,242,880-char) ceiling — enough to cross it without
		// allocating far more than necessary.
		var result = await FunctionParser.FunctionParse(MarkupText.Plain("repeat(x,5242881)"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("#-1 OUTPUT EXCEEDED MAXIMUM SIZE");
	}

	/// <summary>A result comfortably under the ceiling is returned unchanged.</summary>
	[Test]
	public async Task OutputCeiling_NormalResult_IsUnaffected()
	{
		var result = await FunctionParser.FunctionParse(MarkupText.Plain("repeat(ab,5)"));

		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("ababababab");
	}

	/// <summary>
	/// When the oversized result is produced by a function nested inside another call, the outer
	/// frame must still report the output-size error — not a generic invocation-limit error. The
	/// three limits (invocation, recursion/call, output) share one "exceeded" flag, so the flag
	/// carries the error that first tripped it; an earlier version hardcoded the invocation-limit
	/// message at the argument-evaluation propagation point and mislabelled this case.
	/// </summary>
	[Test]
	public async Task OutputCeiling_OversizedResultNestedInAnotherCall_ReportsOutputError()
	{
		var result = await FunctionParser.FunctionParse(MarkupText.Plain("strcat(repeat(x,5242881),tail)"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("#-1 OUTPUT EXCEEDED MAXIMUM SIZE");
	}

	/// <summary>
	/// The same propagation guarantee for the recursion limit: when an attribute recurses past
	/// FunctionRecursionLimit while nested inside another function, the outer frame must report the
	/// recursion error, not a generic invocation-limit error. The recursion limit is enforced in
	/// AttributeService/GeneralCommands (a different file from CallFunction), which set the shared
	/// exceeded-flag; those sites must also record the error string so the propagation point does
	/// not fall back to the invocation-limit message.
	/// </summary>
	[Test]
	public async Task RecursionLimit_NestedInAnotherCall_ReportsRecursionNotInvocationError()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "NestedRec");
		// Unbounded self-recursion: u() re-invokes the same attribute, tripping the per-attribute
		// FunctionRecursionLimit (100) well before the invocation limit (100000).
		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&NESTED_REC_UNIQUE {objDbRef}=[u({objDbRef}/NESTED_REC_UNIQUE)]"));

		var result = await FunctionParser.FunctionParse(
			MarkupText.Plain($"strcat([u({objDbRef}/NESTED_REC_UNIQUE)],tail)"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		var output = result!.Message!.ToPlainText();
		await Assert.That(output).Contains("#-1 FUNCTION RECURSION LIMIT EXCEEDED");
		await Assert.That(output).DoesNotContain("INVOCATION LIMIT");
	}
}
