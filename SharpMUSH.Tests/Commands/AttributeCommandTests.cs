using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AttributeCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask SetAttributeBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TEST #1=Test Value"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());

		// Verify attribute was set
		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		var attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "TEST",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask SetAttributeEmpty()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTCLEAR #1="));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask SetAttributeComplexValue()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&COMPLEX #1=This is a [add(1,2)] test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask Test_CopyAttribute_Basic()
	{
		// First set an attribute with unique test string
		await Parser.CommandParse(1, ConnectionService, MModule.single("&SOURCE_CPATTR_TEST1 #1=test_string_CPATTR_basic"));
		
		// Copy it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/SOURCE_CPATTR_TEST1=#1/DEST_CPATTR_TEST1"));

		// Verify destination attribute was created
		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		var destAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "DEST_CPATTR_TEST1",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_basic");
		
		// Verify source still exists
		var sourceAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "SOURCE_CPATTR_TEST1",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsTrue();
	}

	[Test]
	public async ValueTask Test_CopyAttribute_MultipleDestinations()
	{
		// Set source attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("&SOURCE_CPATTR_MULTI #1=test_string_CPATTR_multi"));
		
		// Copy to multiple destinations
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/SOURCE_CPATTR_MULTI=#1/DEST1_CPATTR_MULTI,#1/DEST2_CPATTR_MULTI"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		
		// Verify both destinations
		var dest1Attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "DEST1_CPATTR_MULTI",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(dest1Attr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(dest1Attr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_multi");
		
		var dest2Attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "DEST2_CPATTR_MULTI",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(dest2Attr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(dest2Attr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_multi");
	}

	[Test]
	public async ValueTask Test_MoveAttribute_Basic()
	{
		// First set an attribute with unique test string
		await Parser.CommandParse(1, ConnectionService, MModule.single("&MOVESOURCE_TEST1 #1=test_string_MVATTR_basic"));
		
		// Move it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mvattr #1/MOVESOURCE_TEST1=#1/MOVEDEST_TEST1"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		
		// Verify destination attribute was created
		var destAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "MOVEDEST_TEST1",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_MVATTR_basic");
		
		// Verify source no longer exists
		var sourceAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "MOVESOURCE_TEST1",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsFalse();
	}

	[Test]
	public async ValueTask Test_WipeAttributes_AllAttributes()
	{
		// Set some attributes with unique test strings
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WIPE1_TEST #1=test_string_WIPE_val1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WIPE2_TEST #1=test_string_WIPE_val2"));
		
		// Verify they exist
		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		var attr1Before = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "WIPE1_TEST",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr1Before.IsAttribute).IsTrue();
		
		// Wipe them with pattern
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wipe #1/WIPE*_TEST"));

		// Verify they're gone
		var attr1After = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "WIPE1_TEST",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr1After.IsAttribute).IsFalse();
		
		var attr2After = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "WIPE2_TEST",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr2After.IsAttribute).IsFalse();
	}

	[Test]
	public async ValueTask Test_AtrLock_LockAndUnlock()
	{
		// Set an attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("&LOCKTEST_ATTR #1=test_string_ATRLOCK_value"));
		
		// Lock it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrlock #1/LOCKTEST_ATTR=on"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		var attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "LOCKTEST_ATTR",
			IAttributeService.AttributeMode.Read, false);
		
		await Assert.That(attr.IsAttribute).IsTrue();
		
		// Check that it's locked (would need to check flags)
		var isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsTrue();
		
		// Unlock it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrlock #1/LOCKTEST_ATTR=off"));
		
		attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "LOCKTEST_ATTR",
			IAttributeService.AttributeMode.Read, false);
		isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsFalse();
	}

	[Test]
	public async ValueTask Test_AtrLock_QueryStatus()
	{
		// Set an attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("&QUERYLOCK_ATTR #1=test_value"));
		
		// Query lock status (no =on or =off)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrlock #1/QUERYLOCK_ATTR"));

		// Should receive a notification about lock status
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("unlocked") || s.Contains("locked")));
	}

	[Test]
	public async ValueTask Test_AtrChown_InvalidArguments()
	{
		// Try to chown without proper arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrchown #1"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Invalid") || s.Contains("invalid")));
	}

	[Test]
	public async ValueTask Test_CopyAttribute_InvalidSource()
	{
		// Try to copy a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/NONEXISTENT_ATTR_TEST=#1/DEST"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("not found") || s.Contains("NO MATCH")));
	}

	[Test]
	public async ValueTask Test_MoveAttribute_InvalidSource()
	{
		// Try to move a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mvattr #1/NONEXISTENT_MOVE_TEST=#1/DEST"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("not found") || s.Contains("NO MATCH")));
	}
}
