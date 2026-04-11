using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class AttributeCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async ValueTask SetAttributeBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetAttrBasic");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objName = obj.Known.Object().Name;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TEST_ATTRSET_UNIQUE {objDbRef}=Test Value"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, $"{objName}/TEST_ATTRSET_UNIQUE - Set.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		// Verify attribute was set
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "TEST_ATTRSET_UNIQUE",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();
	}

	[Test]
	public async ValueTask SetAttributeEmpty()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetAttrEmpty");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objName = obj.Known.Object().Name;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TESTCLEAR_ATTRSET_UNIQUE {objDbRef}="));

		// Setting to empty still says "Set." per parser visitor implementation
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, $"{objName}/TESTCLEAR_ATTRSET_UNIQUE - Set.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask SetAttributeComplexValue()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetAttrComplex");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objName = obj.Known.Object().Name;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&COMPLEX {objDbRef}=This is a [add(1,2)] test"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, $"{objName}/COMPLEX - Set.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// PennMUSH compatibility: braces inside NoParse attribute storage must be preserved.
	/// Without this fix, <c>&amp;CMD obj=$cmd *:@switch expr=1,{body1},{body2}</c> would lose
	/// the braces around <c>body1</c> and <c>body2</c>, breaking @switch flow control when the
	/// command pattern is later triggered.
	/// </summary>
	[Test]
	public async ValueTask SetAttribute_PreservesBracesInNoParse()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetAttrBrace");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var attrName = $"CMD_BRACE_{uniqueId}";

		// Store a $command pattern with braces around @switch case bodies
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&{attrName} {objDbRef}=$+test_{uniqueId} *:@switch hasflag(%#,wizard)=1,{{@pemit %#=yes}},{{@pemit %#=no}}"));

		// Verify the attribute was stored with braces intact
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, attrName,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because($"&{attrName} should have been set");

		var attrValue = attr.AsAttribute.Last().Value.ToPlainText();

		// The braces around @pemit bodies must survive NoParse attribute storage
		await Assert.That(attrValue).Contains("{@pemit %#=yes}")
			.Because("braces around @switch case bodies must be preserved in attribute storage");
		await Assert.That(attrValue).Contains("{@pemit %#=no}")
			.Because("braces around @switch case bodies must be preserved in attribute storage");
	}

	[Test]
	public async ValueTask Test_CopyAttribute_Direct()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object and set attribute directly via database
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrDirect");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["SOURCE_DIRECT_CPATTR"], A.single("test_string_CPATTR_direct"), owner);

		// Verify source exists
		var sourceAttr = Database.GetAttributeAsync(objDbRef, ["SOURCE_DIRECT_CPATTR"]);
		var sourceList = await sourceAttr!.ToListAsync();
		await Assert.That(sourceList).Count().IsEqualTo(1);

		// Copy it using command
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/SOURCE_DIRECT_CPATTR={objDbRef}/DEST_DIRECT_CPATTR"));

		// Verify command sent a success notification with unique attribute name
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Attribute copied to 1 destination."))
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		// Verify destination attribute was created
		var destAttr = Database.GetAttributeAsync(objDbRef, ["DEST_DIRECT_CPATTR"]);
		var destList = destAttr == null ? null : await destAttr.ToListAsync();

		await Assert.That(destList).IsNotNull();
		if (destList != null)
		{
			await Assert.That(destList.Last().Value.ToString()).IsEqualTo("test_string_CPATTR_direct");
		}
	}

	[Test]
	public async ValueTask Test_CopyAttribute_Basic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrBasic");

		// First set an attribute with unique test string
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&SOURCE_CPATTR_BASIC {objDbRef}=test_string_CPATTR_basic_unique"));

		// Copy it
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/SOURCE_CPATTR_BASIC={objDbRef}/DEST_CPATTR_BASIC"));

		// Verify command executed with success notification mentioning destination.
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				"Attribute copied to 1 destination."
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		// Verify destination attribute was created
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var destAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "DEST_CPATTR_BASIC",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_basic_unique");

		// Verify source still exists
		var sourceAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "SOURCE_CPATTR_BASIC",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsTrue();
	}

	[Test]
	public async ValueTask Test_CopyAttribute_MultipleDestinations()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrMulti");

		// Set source attribute with unique name
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&SOURCE_CPATTR_MULTI_UNIQUE {objDbRef}=test_string_CPATTR_multi_value"));

		// Copy to multiple destinations
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/SOURCE_CPATTR_MULTI_UNIQUE={objDbRef}/DEST1_CPATTR_MULTI,{objDbRef}/DEST2_CPATTR_MULTI"));

		// Verify command executed successfully with notification mentioning 2 destinations
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Attribute copied to 2 destinations."))
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Verify both destinations
		var dest1Attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "DEST1_CPATTR_MULTI",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(dest1Attr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(dest1Attr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_multi_value");

		var dest2Attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "DEST2_CPATTR_MULTI",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(dest2Attr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(dest2Attr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_multi_value");
	}

	[Test]
	public async ValueTask Test_MoveAttribute_Basic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MvAttrBasic");

		// First set an attribute with unique test string
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&MOVESOURCE_UNIQUE {objDbRef}=test_string_MVATTR_basic_moved"));

		// Move it
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mvattr {objDbRef}/MOVESOURCE_UNIQUE={objDbRef}/MOVEDEST_UNIQUE"));

		// Verify command executed successfully with notification about move
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Attribute moved to 1 destination."))
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Verify destination attribute was created
		var destAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "MOVEDEST_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_MVATTR_basic_moved");

		// Verify source no longer exists
		var sourceAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "MOVESOURCE_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsFalse();
	}

	[Test]
	public async ValueTask Test_WipeAttributes_AllAttributes()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WipeAttrs");

		// Set some attributes with unique test strings
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&WIPE1_UNIQUE {objDbRef}=test_string_WIPE_val1_unique"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&WIPE2_UNIQUE {objDbRef}=test_string_WIPE_val2_unique"));

		// Verify they exist
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr1Before = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "WIPE1_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr1Before.IsAttribute).IsTrue();

		// Wipe them with pattern
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@wipe {objDbRef}/WIPE*_UNIQUE"));

		// Verify command sent notification about wiping with the pattern
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Wiped attributes matching WIPE*_UNIQUE."))
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		// Verify they're gone
		var attr1After = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "WIPE1_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr1After.IsAttribute).IsFalse();

		var attr2After = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "WIPE2_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr2After.IsAttribute).IsFalse();
	}

	[Test]
	public async ValueTask Test_AtrLock_LockAndUnlock()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AtrLock");

		// Set an attribute with unique name
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LOCKTEST_UNIQUE_ATTR {objDbRef}=test_string_ATRLOCK_value_unique"));

		// Lock it
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrlock {objDbRef}/LOCKTEST_UNIQUE_ATTR=on"));

		// Verify lock notification sent
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Attribute locked."))
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "LOCKTEST_UNIQUE_ATTR",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();

		// Check that it's locked (would need to check flags)
		var isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsTrue();

		// Unlock it
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrlock {objDbRef}/LOCKTEST_UNIQUE_ATTR=off"));

		// Verify unlock notification sent
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Attribute unlocked."))
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "LOCKTEST_UNIQUE_ATTR",
			IAttributeService.AttributeMode.Read, false);
		isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsFalse();
	}

	[Test]
	public async ValueTask Test_AtrLock_QueryStatus()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AtrLockQuery");

		// Set an attribute with unique name
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&QUERYLOCK_UNIQUE_ATTR {objDbRef}=test_value_unique_query"));

		// Query lock status (no =on or =off)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrlock {objDbRef}/QUERYLOCK_UNIQUE_ATTR"));

		// Should receive a notification about lock status with the attribute name
		await NotifyService
			.Received()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("That attribute is unlocked."))
			, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_AtrChown_InvalidArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AtrChownInvalid");

		// Try to chown without proper arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrchown {objDbRef}"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("You need to give an object/attribute pair.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_CopyAttribute_InvalidSource()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrInvalid");

		// Try to copy a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/NONEXISTENT_ATTR_TEST={objDbRef}/DEST"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Attribute NONEXISTENT_ATTR_TEST not found on source object.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_MoveAttribute_InvalidSource()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MvAttrInvalid");

		// Try to move a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mvattr {objDbRef}/NONEXISTENT_MOVE_TEST={objDbRef}/DEST"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("Attribute NONEXISTENT_MOVE_TEST not found on source object.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Edit_SimpleReplace()
	{
		// Create an isolated test object and set up attribute
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditSimple");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_TEST"], A.single("Hello World"), owner);

		// Edit it - replace "World" with "Universe"
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_TEST=World,Universe"));

		// Verify the attribute was changed
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Hello Universe");
	}

	[Test]
	public async ValueTask Test_Edit_Append()
	{
		// Create an isolated test object and set up attribute
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditAppend");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_APPEND_TEST"], A.single("Start"), owner);

		// Edit it - append " End" (use braces to preserve leading space)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_APPEND_TEST=$,{{ End}}"));

		// Verify the attribute was changed
		// Note: RSArgs parser trims whitespace from arguments, so " End" becomes "End"
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_APPEND_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("StartEnd");
	}

	[Test]
	public async ValueTask Test_Edit_Prepend()
	{
		// Create an isolated test object and set up attribute
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditPrepend");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_PREPEND_TEST"], A.single("End"), owner);

		// Edit it - prepend "Start "
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_PREPEND_TEST=^,Start "));

		// Verify the attribute was changed
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_PREPEND_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Start End");
	}

	[Test]
	public async ValueTask Test_Edit_FirstOnly()
	{
		// Create an isolated test object and set up attribute with repeated text
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditFirst");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_FIRST_TEST"], A.single("foo bar foo baz"), owner);

		// Edit it - replace only first "foo"
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit/first {objDbRef}/EDIT_FIRST_TEST=foo,qux"));

		// Verify only first occurrence was replaced
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_FIRST_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("qux bar foo baz");
	}

	[Test]
	public async ValueTask Test_Edit_ReplaceAll()
	{
		// Create an isolated test object and set up attribute with repeated text
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditAll");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_ALL_TEST"], A.single("foo bar foo baz"), owner);

		// Edit it - replace all "foo"
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_ALL_TEST=foo,qux"));

		// Verify all occurrences were replaced
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_ALL_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("qux bar qux baz");
	}

	[Test]
	public async ValueTask Test_Edit_Check_NoChange()
	{
		// Create an isolated test object and set up attribute
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditCheck");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_CHECK_TEST"], A.single("Original"), owner);

		// Edit with /check - should preview but not change
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit/check {objDbRef}/EDIT_CHECK_TEST=Original,Changed"));

		// Verify the attribute was NOT changed
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_CHECK_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Original");
	}

	[Test]
	public async ValueTask Test_Edit_Regex()
	{
		// Create an isolated test object and set up attribute
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditRegex");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_REGEX_TEST"], A.single("foo123bar"), owner);

		// Edit with regex - replace digits
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit/regexp {objDbRef}/EDIT_REGEX_TEST=\\\\d+,XXX"));

		// Verify the regex replacement worked
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_REGEX_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("fooXXXbar");
	}

	[Test]
	public async ValueTask Test_Edit_NoMatch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an isolated test object (attribute does not exist on it)
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditNoMatch");

		// Try to edit a non-existent attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/NONEXISTENT_EDIT_TEST=foo,bar"));

		// Should receive error notification
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg => msg.IsT1 && msg.AsT1.Contains("No matching attributes found.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
