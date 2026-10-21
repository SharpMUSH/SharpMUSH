namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What <c>@function</c> shows: the listing (PennMUSH's <c>do_function</c>, <c>src/function.c</c>) and one
/// function's report (<c>build_function_report</c>), each a titled panel.
/// </summary>
public class FunctionListingTests : ServerTestBase
{
	[Test]
	public async Task ListingShowsWhereEachFunctionLivesToWhoeverMayDefineThem()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("listed").ToLowerInvariant();
		var holder = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FnHolder");
		await Cmd($"&FUN`BODY {holder}=%0");
		await Cmd($"@function {name}={holder},FUN`BODY,1,2");
		await Cmd($"@function/args {name}=title width");
		await Cmd($"@function/disable {name}");
		try
		{
			var listing = await Cmd("@function");
			await Assert.That(listing).Contains("Global functions");
			var row = listing.Split('\n').First(line => line.Contains(name, StringComparison.Ordinal));
			await Assert.That(row).Contains($"{name}(title, width)");
			await Assert.That(row).Contains($"#{holder.Number}/FUN`BODY");
			await Assert.That(row).Contains("1-2");
			await Assert.That(row).Contains("disabled");
			await Assert.That(listing).Contains("built-in");
		}
		finally
		{
			await Cmd($"@function/delete {name}");
		}
	}

	[Test]
	public async Task AMortalSeesTheNamesOnly()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("named").ToLowerInvariant();
		var holder = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FnHolder");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "FnMortal");
		await Cmd($"&FUN`BODY {holder}=%0");
		await Cmd($"@function {name}={holder},FUN`BODY");
		try
		{
			var listing = await CmdAs(mortal.DbRef, mortal.Handle, "@function");
			await Assert.That(listing).Contains(name);
			await Assert.That(listing).DoesNotContain($"#{holder.Number}/FUN`BODY");

			var report = await CmdAs(mortal.DbRef, mortal.Handle, $"@function {name}");
			await Assert.That(report).Contains($"{name}()");
			await Assert.That(report).DoesNotContain("Location");
		}
		finally
		{
			await Cmd($"@function/delete {name}");
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	public async Task ReportGivesPennMushArgumentCounts()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("report").ToLowerInvariant();
		var holder = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FnHolder");
		await Cmd($"&FUN`BODY {holder}=%0");
		await Cmd($"@function {name}={holder},FUN`BODY,0,2");
		try
		{
			var report = await Cmd($"@function {name}");
			await Assert.That(report).Contains($"{name}()");
			await Assert.That(report).Contains($"#{holder.Number}/FUN`BODY");
			await Assert.That(report).Contains("0 to 2");
			await Assert.That(report).Contains("enabled");
		}
		finally
		{
			await Cmd($"@function/delete {name}");
		}

		var builtin = await Cmd("@function words");
		await Assert.That(builtin).Contains("words()");
		await Assert.That(builtin).Contains("built-in");
		await Assert.That(builtin).Contains("1 to 2");
	}
}
