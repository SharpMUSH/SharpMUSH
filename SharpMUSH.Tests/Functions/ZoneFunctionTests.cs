using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

[NotInParallel]
public class ZoneFunctionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task ZoneGetNoZone()
	{
		// Create an object without a zone
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncTest1"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Get zone should return #-1 for objects with no zone
		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneGetNoZone))]
	public async Task ZoneGetWithZone()
	{
		// Create a zone master object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Create an object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncTest2"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set the zone via command
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));
		
		// Get zone should return the zone dbref
		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var resultDbRef = DBRef.Parse(result.ToPlainText()!);
		
		await Assert.That(resultDbRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ZoneGetWithZone))]
	public async Task ZoneSetWithFunction()
	{
		// Create a zone master object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncSetMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Create an object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncSetTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set the zone via function (requires side effects enabled)
		var setResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})")))?.Message!;
		
		// zone() with 2 args returns empty string on success
		await Assert.That(setResult.ToPlainText()).IsEqualTo("");
		
		// Verify the zone was actually set
		var getResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var resultDbRef = DBRef.Parse(getResult.ToPlainText()!);
		
		await Assert.That(resultDbRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ZoneSetWithFunction))]
	public async Task ZoneClearWithFunction()
	{
		// Create a zone master object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncClearMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Create an object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncClearTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set the zone first
		await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})"));
		
		// Clear the zone using "none"
		var clearResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},none)")))?.Message!;
		await Assert.That(clearResult.ToPlainText()).IsEqualTo("");
		
		// Verify the zone was cleared
		var getResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		await Assert.That(getResult.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneClearWithFunction))]
	public async Task ZoneInvalidObject()
	{
		// Try to get zone of invalid object
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(#99999)")))?.Message!;
		
		// Should return an error
		await Assert.That(result.ToPlainText()).Matches("^#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneInvalidObject))]
	public async Task ZoneNoPermissionToExamine()
	{
		// Create an object that player can examine (they created it)
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZonePermTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Player can examine their own objects, so this should work
		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		
		// Should return #-1 (no zone) rather than permission denied
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneNoPermissionToExamine))]
	public async Task ZoneOnPlayer()
	{
		// Test getting zone of current player
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(%#)")))?.Message!;
		
		// Should return #-1 or a zone dbref
		await Assert.That(result.ToPlainText()).Matches("^(#-1|#[0-9]+:[0-9]+)$");
	}

	[Test]
	[DependsOn(nameof(ZoneOnPlayer))]
	public async Task ZoneOnRoom()
	{
		// Test getting zone of current room
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(%l)")))?.Message!;
		
		// Should return #-1 or a zone dbref
		await Assert.That(result.ToPlainText()).Matches("^(#-1|#[0-9]+:[0-9]+)$");
	}

	[Test]
	[DependsOn(nameof(ZoneOnRoom))]
	public async Task ZoneChainTest()
	{
		// Create a hierarchy: Zone -> Object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChainZoneMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChainZonedObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set zone
		await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})"));
		
		// Verify object has zone
		var objZone = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var objZoneDbRef = DBRef.Parse(objZone.ToPlainText()!);
		await Assert.That(objZoneDbRef.Number).IsEqualTo(zoneDbRef.Number);
		
		// Verify zone master itself has no zone (should be #-1)
		var zoneOfZone = (await FunctionParser.FunctionParse(MModule.single($"zone({zoneDbRef})")))?.Message!;
		await Assert.That(zoneOfZone.ToPlainText()).IsEqualTo("#-1");
	}
}
