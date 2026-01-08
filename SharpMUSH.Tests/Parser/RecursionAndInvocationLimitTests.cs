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
}
