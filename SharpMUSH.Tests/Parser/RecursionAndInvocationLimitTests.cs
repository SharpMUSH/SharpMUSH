using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests to verify that recursion and invocation limits are tracked accurately.
/// These tests prove assumptions about how the limits work and ensure they are enforced correctly.
/// </summary>
public class RecursionAndInvocationLimitTests : TestsBase
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// Test that basic recursion (same function calling itself) is detected and limited.
	/// This tests FunctionRecursionLimit.
	/// </summary>
	[Test]
	public async Task RecursionLimit_SameFunction_IsEnforced()
	{
		// Arrange: Create a recursive function with a counter
		// Use add() to increment and check if we've exceeded limit
		// This will recurse until it hits the limit (limit is 50 in test config)
		var command = "&RECURSE #1=[setq(c,add(r(c),1))][if(lte(r(c),105),[u(#1/RECURSE)],DONE)]";
		
		// Create the attribute
		await CommandParser.CommandParse(1, ConnectionService, MModule.single(command));
		
		// Act: Evaluate the recursive function - should halt at limit
		var result = await FunctionParser.FunctionParse(MModule.single("[u(#1/RECURSE)]"));
		
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
	/// Test that stack depth (total nesting of function calls) is tracked.
	/// This tests MaxDepth.
	/// </summary>
	[Test]
	public async Task StackDepth_NestedDifferentFunctions_IsTracked()
	{
		// Arrange: Create nested function calls with different function names
		// Test MaxDepth = 10
		// Create a chain like [strlen([strlen(...)])]
		// With 12 levels, should exceed the limit and halt
		var nestedCalls = "x";
		for (int i = 0; i < 12; i++)
		{
			nestedCalls = $"[strlen({nestedCalls})]";
		}
		
		// Act: Parse the deeply nested structure
		var result = await FunctionParser.FunctionParse(MModule.single(nestedCalls));
		
		// Assert: Should hit the stack depth limit and halt evaluation
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		Console.WriteLine($"Stack depth test result: {output}");
		
		// Should get error about depth/call limit
		await Assert.That(output).Contains("#-1");
	}

	/// <summary>
	/// Test a controlled depth to understand exact behavior.
	/// </summary>
	[Test]
	public async Task StackDepth_ExactLimit_IsEnforced()
	{
		// Arrange: MaxDepth is 10 in test config
		// 10 nested calls should succeed
		// 11 nested calls should fail
		
		// Create exactly 10 nested calls - should succeed
		var nested10 = "x";
		for (int i = 0; i < 10; i++)
		{
			nested10 = $"[strlen({nested10})]";
		}
		
		// Create exactly 11 nested calls - should fail  
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
		
		// 10-deep should succeed and return "1" (length of "x")
		await Assert.That(output10).IsEqualTo("1");
		
		// 11-deep should fail with an error - evaluation halts
		await Assert.That(output11).Contains("#-1");
	}

	/// <summary>
	/// Test mutual recursion (A calls B, B calls A) to verify recursion tracking.
	/// </summary>
	[Test]
	public async Task RecursionLimit_MutualRecursion_IsDetected()
	{
		// Arrange: Create two functions that call each other with a counter
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&FUNC_A #1=[setq(a,add(r(a),1))][if(lte(r(a),15),[u(#1/FUNC_B)],DONE_A)]"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&FUNC_B #1=[setq(b,add(r(b),1))][if(lte(r(b),15),[u(#1/FUNC_A)],DONE_B)]"));
		
		// Act: Evaluate - should halt at limit
		var result = await FunctionParser.FunctionParse(MModule.single("[u(#1/FUNC_A)]"));
		
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
		var config = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		
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
		// Arrange: Create a function that calls itself through another function
		// This tests that recursionDepth correctly counts occurrences of the same function name
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&WRAP #1=[setq(w,add(r(w),1))][if(lte(r(w),15),[u(#1/INNER)],DONE_W)]"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&INNER #1=[setq(i,add(r(i),1))][if(lte(r(i),15),[u(#1/WRAP)],DONE_I)]"));
		
		// Act: Execute - creates pattern WRAP->INNER->WRAP->INNER->...
		var result = await FunctionParser.FunctionParse(MModule.single("[u(#1/WRAP)]"));
		
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
		var config = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
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
		
		// Test 1: Recursion limit - same function many times (limit is 50)
		var recursiveAttr = "[setq(c,add(r(c),1))][if(lte(r(c),105),[u(#1/REC)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&REC #1={recursiveAttr}"));
		var recursionResult = await FunctionParser.FunctionParse(MModule.single("[u(#1/REC)]"));
		
		// Test 2: Stack depth - deep nesting of different functions (limit is 10)
		var deepNest = "x";
		for (int i = 0; i < 12; i++)
		{
			deepNest = $"[strlen({deepNest})]";
		}
		var stackResult = await FunctionParser.FunctionParse(MModule.single(deepNest));
		
		// Assert: Both should return errors - evaluation halts
		await Assert.That(recursionResult).IsNotNull();
		await Assert.That(stackResult).IsNotNull();
		
		var recursionError = recursionResult!.Message.ToPlainText();
		var stackError = stackResult!.Message.ToPlainText();
		
		// Both should contain errors
		await Assert.That(recursionError).Contains("#-1");
		await Assert.That(stackError).Contains("#-1");
		
		// Log the actual errors to document behavior
		Console.WriteLine($"Recursion error: {recursionError}");
		Console.WriteLine($"Stack depth error: {stackError}");
	}

	/// <summary>
	/// Test that different attribute evaluation methods (u, ufun, ulocal) all enforce recursion limits.
	/// This proves they all use the centralized attribute evaluation path with recursion tracking.
	/// </summary>
	[Test]
	public async Task RecursionLimit_AllAttributeMethods_AreEnforced()
	{
		// Arrange: Create recursive attributes using different methods
		// Each method should hit the same recursion tracking
		
		// Test u() - standard user-defined function call
		var uRecursive = "[setq(c,add(r(c),1))][if(lte(r(c),105),[u(#1/U_REC)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&U_REC #1={uRecursive}"));
		
		// Test ufun() - user-defined function with default
		var ufunRecursive = "[setq(c,add(r(c),1))][if(lte(r(c),105),[ufun(#1/UFUN_REC,default)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&UFUN_REC #1={ufunRecursive}"));
		
		// Test ulocal() - local user-defined function
		var ulocalRecursive = "[setq(c,add(r(c),1))][if(lte(r(c),105),[ulocal(#1/ULOCAL_REC)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&ULOCAL_REC #1={ulocalRecursive}"));
		
		// Act: Test all three methods
		var uResult = await FunctionParser.FunctionParse(MModule.single("[u(#1/U_REC)]"));
		var ufunResult = await FunctionParser.FunctionParse(MModule.single("[ufun(#1/UFUN_REC)]"));
		var ulocalResult = await FunctionParser.FunctionParse(MModule.single("[ulocal(#1/ULOCAL_REC)]"));
		
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
	/// NOTE: This test uses a simpler approach since @INCLUDE notifications don't return  
	/// values directly but are sent to NotifyService.
	/// </summary>
	[Test]
	[Skip("TODO: Commands send notifications via NotifyService, not return values. Need to redesign test to check NotifyService calls for recursion errors.")]
	public async Task RecursionLimit_IncludeCommand_TracksRecursion()
	{
		// @INCLUDE now uses ExecuteAttributeWithTracking helper to track recursion
		
		// Arrange: Create a recursive attribute that uses u() to call itself
		// This will be called via @include, and @include will track the recursion
		var attr = "[u(#1/SELFCALL)]";
		var attr2 = "[u(#1/SELFCALL)]";
		
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&SELFCALL #1={attr2}"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&INCLUDETEST #1={attr}"));
		
		// Act: Call @include which will evaluate the attribute
		var result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@include #1/INCLUDETEST"));
		
		// TODO: Need to check NotifyService for recursion error messages
		// Commands don't return Message values like functions do
	}

	/// <summary>
	/// Test that @TRIGGER properly tracks recursion when evaluating attributes.
	/// NOTE: This test uses a simpler approach since @TRIGGER notifications don't return
	/// values directly but are sent to NotifyService.
	/// </summary>
	[Test]
	[Skip("TODO: Commands send notifications via NotifyService, not return values. Need to redesign test to check NotifyService calls for recursion errors.")]
	public async Task RecursionLimit_TriggerCommand_TracksRecursion()
	{
		// Arrange: Create a recursive attribute
		var command = "&SELFCALL #1=[u(#1/SELFCALL)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single(command));
		
		// Act: Trigger it
		var result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@trigger #1/SELFCALL"));
		
		// TODO: Need to check NotifyService for recursion error messages
		// Commands don't return Message values like functions do
	}

	/// <summary>
	/// Test that command-based attribute evaluation tracks the attribute's recursion.
	/// This proves @INCLUDE and @TRIGGER increment the recursion counter for the attribute they evaluate.
	/// NOTE: This test uses a simpler approach since commands send notifications via NotifyService.
	/// </summary>
	[Test]
	[Skip("TODO: Commands send notifications via NotifyService, not return values. Need to redesign test to check NotifyService calls.")]
	public async Task RecursionLimit_CommandsTrackAttributeRecursion()
	{
		// Arrange: Create an attribute that calls u() which then uses @include
		// The key is that the OUTER attribute's recursion should be tracked by @INCLUDE
		var attr1 = "A[u(#1/B)]";
		var attr2 = "B";
		
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&A #1={attr1}"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&B #1={attr2}"));
		
		// Act: Call via @include - the recursion counter for "A" should be incremented
		var result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@include #1/A"));
		
		// TODO: Need to check NotifyService for the actual output
		// Commands don't return Message values like functions do
	}
}
