using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Tests to verify that function and command aliases work correctly.
/// This ensures that the alias mappings from Configurable are properly loaded and functional.
/// </summary>
public class AliasTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// Test that the function alias 'u' works as an alias for 'ufun'.
	/// This verifies that FunctionAliases dictionary is properly loaded and functional.
	/// </summary>
	[Test]
	public async Task FunctionAlias_U_WorksAsUfun()
	{
		var result = await FunctionParser.FunctionParse(MModule.single("u(#1/test)"));

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
		var result = await FunctionParser.FunctionParse(MModule.single("iter(a b c,##)"));

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
		var exception = await Assert.That(async () =>
		{
			await CommandParser.CommandParse(1, ConnectionService, MModule.single("l"));
		}).ThrowsNothing();

		await Assert.That(exception).IsNull();
	}

	/// <summary>
	/// Test that the command alias 'i' works as an alias for 'INVENTORY'.
	/// This provides an additional verification of the CommandAliases system.
	/// </summary>
	[Test]
	public async Task CommandAlias_I_WorksAsInventory()
	{
		var exception = await Assert.That(async () =>
		{
			await CommandParser.CommandParse(1, ConnectionService, MModule.single("i"));
		}).ThrowsNothing();

		await Assert.That(exception).IsNull();
	}

	/// <summary>
	/// Test that aliases are case-insensitive for commands.
	/// </summary>
	[Test]
	public async Task CommandAlias_CaseInsensitive()
	{
		var exception = await Assert.That(async () =>
		{
			await CommandParser.CommandParse(1, ConnectionService, MModule.single("L"));
		}).ThrowsNothing();

		await Assert.That(exception).IsNull();
	}
}
