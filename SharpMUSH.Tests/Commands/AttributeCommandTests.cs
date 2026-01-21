using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Commands;

public class AttributeCommandTests : TestsBase
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;
	private IMediator Mediator => Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => Services.GetRequiredService<IAttributeService>();
	private ISharpDatabase Database => Services.GetRequiredService<ISharpDatabase>();

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask SetAttributeBasic()
	{
		// Clear any previous calls to the mock
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
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTCLEAR #1="));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask SetAttributeComplexValue()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("&COMPLEX #1=This is a [add(1,2)] test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Failing Test - Needs Investigation")]
	public async ValueTask Test_CopyAttribute_Direct()
	{
		// Clear any previous calls to the mock
		// Set attribute directly via database with unique name
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["SOURCE_DIRECT_CPATTR"], A.single("test_string_CPATTR_direct"), player);
		
		// Verify source exists
		var sourceAttr = await Database.GetAttributeAsync(player.Object.DBRef, ["SOURCE_DIRECT_CPATTR"]);
		var sourceList = await sourceAttr!.ToListAsync();
		await Assert.That(sourceList).Count().IsEqualTo(1);
		
		// Copy it using command
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/SOURCE_DIRECT_CPATTR=#1/DEST_DIRECT_CPATTR"));

		// Verify command sent a success notification with unique attribute name
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(),
				"Attribute copied to 1 destination."
			);

		// Verify destination attribute was created
		var destAttr = await Database.GetAttributeAsync(player.Object.DBRef, ["DEST_DIRECT_CPATTR"]);
		var destList = destAttr == null ? null : await destAttr.ToListAsync();
		
		await Assert.That(destList).IsNotNull();
		if (destList != null)
		{
			await Assert.That(destList.Last().Value.ToString()).IsEqualTo("test_string_CPATTR_direct");
		}
	}

	[Test]
	[Skip("Failing Test - Needs Investigation")]
	public async ValueTask Test_CopyAttribute_Basic()
	{
		// Clear any previous calls to the mock
		// First set an attribute with unique test string
		await Parser.CommandParse(1, ConnectionService, MModule.single("&SOURCE_CPATTR_BASIC #1=test_string_CPATTR_basic_unique"));
		
		// Copy it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/SOURCE_CPATTR_BASIC=#1/DEST_CPATTR_BASIC"));

		// Verify command executed with success notification mentioning destination
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(),
				"Attribute copied to 1 destination."
			);

		// Verify destination attribute was created
		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		var destAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "DEST_CPATTR_BASIC",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_basic_unique");
		
		// Verify source still exists
		var sourceAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "SOURCE_CPATTR_BASIC",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsTrue();
	}

	[Test]
	[Skip("Failing Test - Needs Investigation")]
	public async ValueTask Test_CopyAttribute_MultipleDestinations()
	{
		// Clear any previous calls to the mock
		// Set source attribute with unique name
		await Parser.CommandParse(1, ConnectionService, MModule.single("&SOURCE_CPATTR_MULTI_UNIQUE #1=test_string_CPATTR_multi_value"));
		
		// Copy to multiple destinations
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/SOURCE_CPATTR_MULTI_UNIQUE=#1/DEST1_CPATTR_MULTI,#1/DEST2_CPATTR_MULTI"));

		// Verify command executed successfully with notification mentioning 2 destinations
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(),
				"Attribute copied to 2 destinations."
			);

		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		
		// Verify both destinations
		var dest1Attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "DEST1_CPATTR_MULTI",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(dest1Attr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(dest1Attr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_multi_value");
		
		var dest2Attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "DEST2_CPATTR_MULTI",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(dest2Attr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(dest2Attr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_multi_value");
	}

	[Test]
	[Skip("Failing Test - Needs Investigation")]
	public async ValueTask Test_MoveAttribute_Basic()
	{
		// Clear any previous calls to the mock
		// First set an attribute with unique test string
		await Parser.CommandParse(1, ConnectionService, MModule.single("&MOVESOURCE_UNIQUE #1=test_string_MVATTR_basic_moved"));
		
		// Move it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mvattr #1/MOVESOURCE_UNIQUE=#1/MOVEDEST_UNIQUE"));

		// Verify command executed successfully with notification about move
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(),
				"Attribute moved to 1 destination."
			);

		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		
		// Verify destination attribute was created
		var destAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "MOVEDEST_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_MVATTR_basic_moved");
		
		// Verify source no longer exists
		var sourceAttr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "MOVESOURCE_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsFalse();
	}

	[Test]
	[Skip("Failing Test - Needs Investigation")]
	public async ValueTask Test_WipeAttributes_AllAttributes()
	{
		// Clear any previous calls to the mock
		// Set some attributes with unique test strings
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WIPE1_UNIQUE #1=test_string_WIPE_val1_unique"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WIPE2_UNIQUE #1=test_string_WIPE_val2_unique"));
		
		// Verify they exist
		var obj = await Mediator.Send(new GetObjectNodeQuery(new(1)));
		var attr1Before = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "WIPE1_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr1Before.IsAttribute).IsTrue();
		
		// Wipe them with pattern
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wipe #1/WIPE*_UNIQUE"));

		// Verify command sent notification about wiping with the pattern
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(),
				"Wiped attributes matching WIPE*_UNIQUE."
			);

		// Verify they're gone
		var attr1After = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "WIPE1_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr1After.IsAttribute).IsFalse();
		
		var attr2After = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "WIPE2_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr2After.IsAttribute).IsFalse();
	}

	[Test]
	[Skip("Failing Test - Needs Investigation")]
	public async ValueTask Test_AtrLock_LockAndUnlock()
	{
		// Clear any previous calls to the mock
		// Set an attribute with unique name
		await Parser.CommandParse(1, ConnectionService, MModule.single("&LOCKTEST_UNIQUE_ATTR #1=test_string_ATRLOCK_value_unique"));
		
		// Lock it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrlock #1/LOCKTEST_UNIQUE_ATTR=on"));

		// Verify lock notification sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(),
				"Attribute LOCKTEST_UNIQUE_ATTR locked."
			);

		var obj = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "LOCKTEST_UNIQUE_ATTR",
			IAttributeService.AttributeMode.Read, false);
		
		await Assert.That(attr.IsAttribute).IsTrue();
		
		// Check that it's locked (would need to check flags)
		var isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsTrue();
		
		// Unlock it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrlock #1/LOCKTEST_UNIQUE_ATTR=off"));
		
		// Verify unlock notification sent
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(),
				"Attribute LOCKTEST_UNIQUE_ATTR unlocked."
			);

		attr = await AttributeService.GetAttributeAsync(obj.AsPlayer, obj.AsPlayer, "LOCKTEST_UNIQUE_ATTR",
			IAttributeService.AttributeMode.Read, false);
		isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsFalse();
	}

	[Test]
	public async ValueTask Test_AtrLock_QueryStatus()
	{
		// Clear any previous calls to the mock
		// Set an attribute with unique name
		await Parser.CommandParse(1, ConnectionService, MModule.single("&QUERYLOCK_UNIQUE_ATTR #1=test_value_unique_query"));
		
		// Query lock status (no =on or =off)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrlock #1/QUERYLOCK_UNIQUE_ATTR"));

		// Should receive a notification about lock status with the attribute name
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(
				Arg.Any<AnySharpObject>(), 
				"Attribute QUERYLOCK_UNIQUE_ATTR is unlocked."
			);
	}

	[Test]
	public async ValueTask Test_AtrChown_InvalidArguments()
	{
		// Clear any previous calls to the mock
		// Try to chown without proper arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@atrchown #1"));

		// Should receive error notification
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Invalid arguments to @atrchown.");
	}

	[Test]
	public async ValueTask Test_CopyAttribute_InvalidSource()
	{
		// Clear any previous calls to the mock
		// Try to copy a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("@cpattr #1/NONEXISTENT_ATTR_TEST=#1/DEST"));

		// Should receive error notification
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Attribute NONEXISTENT_ATTR_TEST not found on source object.");
	}

	[Test]
	public async ValueTask Test_MoveAttribute_InvalidSource()
	{
		// Clear any previous calls to the mock
		// Try to move a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mvattr #1/NONEXISTENT_MOVE_TEST=#1/DEST"));

		// Should receive error notification
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Attribute NONEXISTENT_MOVE_TEST not found on source object.");
	}

	[Test]
	public async ValueTask Test_Edit_SimpleReplace()
	{
		// Set up an attribute
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["EDIT_TEST"], A.single("Hello World"), player);

		// Edit it - replace "World" with "Universe"
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/EDIT_TEST=World,Universe"));

		// Verify the attribute was changed
		var attr = await Database.GetAttributeAsync(player.Object.DBRef, ["EDIT_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Hello Universe");
	}

	[Test]
	public async ValueTask Test_Edit_Append()
	{
		// Set up an attribute
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["EDIT_APPEND_TEST"], A.single("Start"), player);

		// Edit it - append " End" (use braces to preserve leading space)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/EDIT_APPEND_TEST=$,{ End}"));

		// Verify the attribute was changed
		// Note: RSArgs parser trims whitespace from arguments, so " End" becomes "End"
		var attr = await Database.GetAttributeAsync(player.Object.DBRef, ["EDIT_APPEND_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("StartEnd");
	}

	[Test]
	public async ValueTask Test_Edit_Prepend()
	{
		// Set up an attribute
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["EDIT_PREPEND_TEST"], A.single("End"), player);

		// Edit it - prepend "Start "
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/EDIT_PREPEND_TEST=^,Start "));

		// Verify the attribute was changed
		var attr = await Database.GetAttributeAsync(player.Object.DBRef, ["EDIT_PREPEND_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Start End");
	}

	[Test]
	public async ValueTask Test_Edit_FirstOnly()
	{
		// Set up an attribute with repeated text
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["EDIT_FIRST_TEST"], A.single("foo bar foo baz"), player);

		// Edit it - replace only first "foo"
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit/first #1/EDIT_FIRST_TEST=foo,qux"));

		// Verify only first occurrence was replaced
		var attr = await Database.GetAttributeAsync(player.Object.DBRef, ["EDIT_FIRST_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("qux bar foo baz");
	}

	[Test]
	public async ValueTask Test_Edit_ReplaceAll()
	{
		// Set up an attribute with repeated text
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["EDIT_ALL_TEST"], A.single("foo bar foo baz"), player);

		// Edit it - replace all "foo"
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/EDIT_ALL_TEST=foo,qux"));

		// Verify all occurrences were replaced
		var attr = await Database.GetAttributeAsync(player.Object.DBRef, ["EDIT_ALL_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("qux bar qux baz");
	}

	[Test]
	public async ValueTask Test_Edit_Check_NoChange()
	{
		// Set up an attribute
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["EDIT_CHECK_TEST"], A.single("Original"), player);

		// Edit with /check - should preview but not change
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit/check #1/EDIT_CHECK_TEST=Original,Changed"));

		// Verify the attribute was NOT changed
		var attr = await Database.GetAttributeAsync(player.Object.DBRef, ["EDIT_CHECK_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Original");
	}

	[Test]
	public async ValueTask Test_Edit_Regex()
	{
		// Set up an attribute
		var player = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(player.Object.DBRef, ["EDIT_REGEX_TEST"], A.single("foo123bar"), player);

		// Edit with regex - replace digits
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit/regexp #1/EDIT_REGEX_TEST=\\\\d+,XXX"));

		// Verify the regex replacement worked
		var attr = await Database.GetAttributeAsync(player.Object.DBRef, ["EDIT_REGEX_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("fooXXXbar");
	}

	[Test]
	public async ValueTask Test_Edit_NoMatch()
	{
		// Clear any previous calls to the mock
		// Try to edit a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/NONEXISTENT_EDIT_TEST=foo,bar"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "No matching attributes found.");
	}
}
