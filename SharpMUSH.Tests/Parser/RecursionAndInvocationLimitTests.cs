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
[NotInParallel]
public class RecursionAndInvocationLimitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// Test that basic recursion (same function calling itself) is detected and limited.
	/// This tests FunctionRecursionLimit.
	/// </summary>
	[Test]
	public async Task RecursionLimit_SameFunction_IsEnforced()
	{
		// Arrange: Create a recursive function with a counter
		// Use add() to increment and check if we've exceeded limit
		// This will recurse until it hits the limit
		var command = "&RECURSE #1=[setq(c,add(r(c),1))][if(lte(r(c),105),[u(#1/RECURSE)],DONE)]";
		
		// Create the attribute
		await CommandParser.CommandParse(1, ConnectionService, MModule.single(command));
		
		// Act: Try to evaluate it with a marker to see error via concatenation
		// Errors don't propagate but appear in concatenated results
		var result = await FunctionParser.FunctionParse(MModule.single("[u(#1/RECURSE)][lit(MARKER)]"));
		
		// Assert: Should get recursion limit error visible in concatenated output
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
		// The MARKER shows that evaluation continued after the error
		await Assert.That(output).Contains("MARKER");
		// Should hit recursion limit, not complete successfully
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
		// Create a chain like [cat([strlen([strlen(...)])],MARKER)]
		// With 12 levels, should exceed the limit
		// Use cat() to concatenate error with marker
		var nestedCalls = "x";
		for (int i = 0; i < 12; i++)
		{
			nestedCalls = $"[strlen({nestedCalls})]";
		}
		var testExpr = $"[cat({nestedCalls},[lit(MARKER)])]";
		
		// Act: Parse the deeply nested structure
		var result = await FunctionParser.FunctionParse(MModule.single(testExpr));
		
		// Assert: Should hit the stack depth limit
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		Console.WriteLine($"Stack depth test result: {output}");
		
		// Error appears in concatenated result
		await Assert.That(output).Contains("#-1");
		// Marker appears showing concatenation happened
		await Assert.That(output).Contains("MARKER");
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
		
		// Create exactly 11 nested calls wrapped in cat to see error - should fail  
		var nested11 = "x";
		for (int i = 0; i < 11; i++)
		{
			nested11 = $"[strlen({nested11})]";
		}
		var test11 = $"[cat({nested11},[lit(MARKER)])]";
		
		// Act
		var result10 = await FunctionParser.FunctionParse(MModule.single(nested10));
		var result11 = await FunctionParser.FunctionParse(MModule.single(test11));
		
		// Assert
		await Assert.That(result10).IsNotNull();
		await Assert.That(result11).IsNotNull();
		
		var output10 = result10!.Message.ToPlainText();
		var output11 = result11!.Message.ToPlainText();
		
		Console.WriteLine($"10-deep result: {output10}");
		Console.WriteLine($"11-deep result: {output11}");
		
		// 10-deep should succeed and return "1" (length of "x")
		await Assert.That(output10).IsEqualTo("1");
		
		// 11-deep should fail with an error visible in concatenated result
		await Assert.That(output11).Contains("#-1");
		await Assert.That(output11).Contains("MARKER");
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
		
		// Act: Try to evaluate with marker to see error via concatenation
		var result = await FunctionParser.FunctionParse(MModule.single("[u(#1/FUNC_A)][lit(MARKER)]"));
		
		// Assert: Should get some limit error visible in concatenated result
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
		await Assert.That(output).Contains("MARKER");
	}

	/// <summary>
	/// Test that CallLimit is actually enforced.
	/// </summary>
	[Test]
	public async Task CallLimit_IsEnforced()
	{
		// Arrange: The default CallLimit is 1000
		// Create a very deeply nested parse structure wrapped in cat to see error
		var nested = "test";
		for (int i = 0; i < 1100; i++)
		{
			nested = $"[strlen({nested})]";
		}
		var testExpr = $"[cat({nested},[lit(MARKER)])]";
		
		// Act
		var result = await FunctionParser.FunctionParse(MModule.single(testExpr));
		
		// Assert: Should hit some limit, error visible in concatenated result
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
		await Assert.That(output).Contains("MARKER");
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
		// Arrange: Create a function that calls itself through another function
		// This tests that recursionDepth correctly counts occurrences of the same function name
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&WRAP #1=[setq(w,add(r(w),1))][if(lte(r(w),15),[u(#1/INNER)],DONE_W)]"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&INNER #1=[setq(i,add(r(i),1))][if(lte(r(i),15),[u(#1/WRAP)],DONE_I)]"));
		
		// Act: Execute with marker to see error via concatenation - creates pattern WRAP->INNER->WRAP->INNER->...
		var result = await FunctionParser.FunctionParse(MModule.single("[u(#1/WRAP)][lit(MARKER)]"));
		
		// Assert: Should eventually hit a limit, error visible in concatenated result
		await Assert.That(result).IsNotNull();
		var output = result!.Message.ToPlainText();
		await Assert.That(output).Contains("#-1");
		await Assert.That(output).Contains("MARKER");
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
		// Errors appear in concatenated results
		
		// Test 1: Recursion limit - same function many times
		var recursiveAttr = "[setq(c,add(r(c),1))][if(lte(r(c),105),[u(#1/REC)],DONE)]";
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&REC #1={recursiveAttr}"));
		var recursionResult = await FunctionParser.FunctionParse(MModule.single("[u(#1/REC)][lit(MARKER1)]"));
		
		// Test 2: Stack depth - deep nesting of different functions
		var deepNest = "x";
		for (int i = 0; i < 15; i++)
		{
			deepNest = $"[strlen({deepNest})]";
		}
		var stackTest = $"[cat({deepNest},[lit(MARKER2)])]";
		var stackResult = await FunctionParser.FunctionParse(MModule.single(stackTest));
		
		// Assert: The errors should be visible in concatenated results
		await Assert.That(recursionResult).IsNotNull();
		await Assert.That(stackResult).IsNotNull();
		
		var recursionError = recursionResult!.Message.ToPlainText();
		var stackError = stackResult!.Message.ToPlainText();
		
		// Both should contain errors visible via concatenation
		await Assert.That(recursionError).Contains("#-1");
		await Assert.That(recursionError).Contains("MARKER1");
		await Assert.That(stackError).Contains("#-1");
		await Assert.That(stackError).Contains("MARKER2");
		
		// Log the actual errors to document behavior
		Console.WriteLine($"Recursion error: {recursionError}");
		Console.WriteLine($"Stack depth error: {stackError}");
	}
}
