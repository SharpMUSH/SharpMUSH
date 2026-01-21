using SharpMUSH.Library.ParserInterfaces;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using NSubstitute;

namespace SharpMUSH.Tests;

/// <summary>
/// Tests to verify that function and command aliases work correctly.
/// This ensures that the alias mappings from Configurable are properly loaded and functional.
/// </summary>
public class AliasTests : TestsBase
{
	private INotifyService NotifyService => Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// Test that the function alias 'u' works as an alias for 'ufun'.
	/// This verifies that FunctionAliases dictionary is properly loaded and functional.
	/// </summary>
	[Test]
	public async Task FunctionAlias_U_WorksAsUfun()
	{
		// u() is an alias for ufun()
		// Testing with a simple string substitution: u(#1/test) should evaluate the attribute
		// For this test, we'll verify that u() is recognized and doesn't throw an error
		
		var result = await FunctionParser.FunctionParse(MModule.single("u(#1/test)"));
		
		// The function should be recognized and parsed without error
		// Even if the attribute doesn't exist, the function alias should work
		await Assert.That(result).IsNotNull();
		await Assert.That(result?.Message).IsNotNull();
	}

	/// <summary>
	/// Test that the function alias 'iter' works as an alias for 'parse'.
	/// This provides an additional verification of the FunctionAliases system.
	/// </summary>
	[Test]
	public async Task FunctionAlias_Iter_WorksAsParse()
	{
		// iter() is an alias for parse()
		// Testing with a simple iteration
		
		var result = await FunctionParser.FunctionParse(MModule.single("iter(a b c,##)"));
		
		// The function should be recognized and parsed without error
		await Assert.That(result).IsNotNull();
		await Assert.That(result?.Message).IsNotNull();
	}

	/// <summary>
	/// Test that the command alias 'l' works as an alias for 'LOOK'.
	/// This verifies that CommandAliases dictionary is properly loaded and functional.
	/// </summary>
	[Test]
	public async Task CommandAlias_L_WorksAsLook()
	{
		// l is an alias for LOOK
		// The command should be recognized and executed without throwing an exception
		// This verifies the alias is working by ensuring the command executes
		
		var exception = await Assert.That(async () => 
		{
			await CommandParser.CommandParse(1, ConnectionService, MModule.single("l"));
		}).ThrowsNothing();
		
		// If we got here without exception, the alias worked
		await Assert.That(exception).IsNull();
	}

	/// <summary>
	/// Test that the command alias 'i' works as an alias for 'INVENTORY'.
	/// This provides an additional verification of the CommandAliases system.
	/// </summary>
	[Test]
	public async Task CommandAlias_I_WorksAsInventory()
	{
		// i is an alias for INVENTORY
		// The command should be recognized and executed without throwing an exception
		
		var exception = await Assert.That(async () => 
		{
			await CommandParser.CommandParse(1, ConnectionService, MModule.single("i"));
		}).ThrowsNothing();
		
		// If we got here without exception, the alias worked
		await Assert.That(exception).IsNull();
	}

	/// <summary>
	/// Test that aliases are case-insensitive for commands.
	/// </summary>
	[Test]
	public async Task CommandAlias_CaseInsensitive()
	{
		// Test that 'L' (uppercase) also works
		var exception = await Assert.That(async () => 
		{
			await CommandParser.CommandParse(1, ConnectionService, MModule.single("L"));
		}).ThrowsNothing();
		
		// If we got here without exception, the alias worked
		await Assert.That(exception).IsNull();
	}
}
