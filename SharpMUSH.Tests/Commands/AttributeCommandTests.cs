using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Commands;

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
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, $"{objName}/TEST_ATTRSET_UNIQUE - Set.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

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

		// With empty_attrs=yes (test config), &attr obj= sets to empty (not clear)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TESTCLEAR_ATTRSET_UNIQUE {objDbRef}="));

		await NotifyService
			.Received(1)
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
			.Received(1)
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
	[Retry(3)]
	public async ValueTask SetAttribute_PreservesBracesInNoParse()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetAttrBrace");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var attrName = $"CMD_BRACE_{uniqueId}";

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&{attrName} {objDbRef}=$+test_{uniqueId} *:@switch hasflag(%#,wizard)=1,{{@pemit %#=yes}},{{@pemit %#=no}}"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, attrName,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because($"&{attrName} should have been set");

		var attrValue = attr.AsAttribute.Last().Value.ToPlainText();

		await Assert.That(attrValue).Contains("{@pemit %#=yes}")
			.Because("braces around @switch case bodies must be preserved in attribute storage");
		await Assert.That(attrValue).Contains("{@pemit %#=no}")
			.Because("braces around @switch case bodies must be preserved in attribute storage");
	}

	[Test]
	public async ValueTask Test_CopyAttribute_Direct()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrDirect");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["SOURCE_DIRECT_CPATTR"], A.single("test_string_CPATTR_direct"), owner);

		var sourceAttr = Database.GetAttributeAsync(objDbRef, ["SOURCE_DIRECT_CPATTR"]);
		var sourceList = await sourceAttr!.ToListAsync();
		await Assert.That(sourceList).Count().IsEqualTo(1);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/SOURCE_DIRECT_CPATTR={objDbRef}/DEST_DIRECT_CPATTR"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeCopiedToDestinationsFormat), executor, executor)).IsTrue();

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
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrBasic");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&SOURCE_CPATTR_BASIC {objDbRef}=test_string_CPATTR_basic_unique"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/SOURCE_CPATTR_BASIC={objDbRef}/DEST_CPATTR_BASIC"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeCopiedToDestinationsFormat), executor, executor)).IsTrue();

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var destAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "DEST_CPATTR_BASIC",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_CPATTR_basic_unique");

		var sourceAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "SOURCE_CPATTR_BASIC",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsTrue();
	}

	[Test]
	public async ValueTask Test_CopyAttribute_MultipleDestinations()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrMulti");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&SOURCE_CPATTR_MULTI_UNIQUE {objDbRef}=test_string_CPATTR_multi_value"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/SOURCE_CPATTR_MULTI_UNIQUE={objDbRef}/DEST1_CPATTR_MULTI,{objDbRef}/DEST2_CPATTR_MULTI"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeCopiedToDestinationsFormat), executor, executor)).IsTrue();

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

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
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MvAttrBasic");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&MOVESOURCE_UNIQUE {objDbRef}=test_string_MVATTR_basic_moved"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mvattr {objDbRef}/MOVESOURCE_UNIQUE={objDbRef}/MOVEDEST_UNIQUE"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeMovedToFormat), executor, executor)).IsTrue();

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		var destAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "MOVEDEST_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(destAttr.IsAttribute).IsTrue();
		await Assert.That(MModule.plainText(destAttr.AsAttribute.Last().Value)).IsEqualTo("test_string_MVATTR_basic_moved");

		var sourceAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "MOVESOURCE_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(sourceAttr.IsAttribute).IsFalse();
	}

	[Test]
	public async ValueTask Test_WipeAttributes_AllAttributes()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WipeAttrs");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&WIPE1_UNIQUE {objDbRef}=test_string_WIPE_val1_unique"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&WIPE2_UNIQUE {objDbRef}=test_string_WIPE_val2_unique"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr1Before = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "WIPE1_UNIQUE",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr1Before.IsAttribute).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@wipe {objDbRef}/WIPE*_UNIQUE"));

		// Pattern-based wipe sends WipedAttributes (with the pattern), not AttributesWiped
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.WipedAttributes), executor, executor)).IsTrue();

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
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AtrLock");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LOCKTEST_UNIQUE_ATTR {objDbRef}=test_string_ATRLOCK_value_unique"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrlock {objDbRef}/LOCKTEST_UNIQUE_ATTR=on"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeLocked), executor, executor)).IsTrue();

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "LOCKTEST_UNIQUE_ATTR",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();

		var isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrlock {objDbRef}/LOCKTEST_UNIQUE_ATTR=off"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeUnlocked), executor, executor)).IsTrue();

		attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "LOCKTEST_UNIQUE_ATTR",
			IAttributeService.AttributeMode.Read, false);
		isLocked = attr.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
		await Assert.That(isLocked).IsFalse();
	}

	[Test]
	public async ValueTask Test_AtrLock_QueryStatus()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AtrLockQuery");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&QUERYLOCK_UNIQUE_ATTR {objDbRef}=test_value_unique_query"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrlock {objDbRef}/QUERYLOCK_UNIQUE_ATTR"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeIsUnlocked), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Test_AtrChown_InvalidArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AtrChownInvalid");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@atrchown {objDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Test_CopyAttribute_InvalidSource()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CpAttrInvalid");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cpattr {objDbRef}/NONEXISTENT_ATTR_TEST={objDbRef}/DEST"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeNotFoundOnSourceFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Test_MoveAttribute_InvalidSource()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MvAttrInvalid");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@mvattr {objDbRef}/NONEXISTENT_MOVE_TEST={objDbRef}/DEST"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeNotFoundOnSourceFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Test_Edit_SimpleReplace()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditSimple");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_TEST"], A.single("Hello World"), owner);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_TEST=World,Universe"));

		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Hello Universe");
	}

	[Test]
	public async ValueTask Test_Edit_Append()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditAppend");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_APPEND_TEST"], A.single("Start"), owner);

		// Edit it - append " End" (use braces to preserve leading space)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_APPEND_TEST=$,{{ End}}"));

		// Note: RSArgs parser trims whitespace from arguments, so " End" becomes "End"
		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_APPEND_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("StartEnd");
	}

	[Test]
	public async ValueTask Test_Edit_Prepend()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditPrepend");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_PREPEND_TEST"], A.single("End"), owner);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_PREPEND_TEST=^,Start "));

		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_PREPEND_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Start End");
	}

	[Test]
	public async ValueTask Test_Edit_FirstOnly()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditFirst");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_FIRST_TEST"], A.single("foo bar foo baz"), owner);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit/first {objDbRef}/EDIT_FIRST_TEST=foo,qux"));

		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_FIRST_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("qux bar foo baz");
	}

	[Test]
	public async ValueTask Test_Edit_ReplaceAll()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditAll");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_ALL_TEST"], A.single("foo bar foo baz"), owner);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/EDIT_ALL_TEST=foo,qux"));

		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_ALL_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("qux bar qux baz");
	}

	[Test]
	public async ValueTask Test_Edit_Check_NoChange()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditCheck");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_CHECK_TEST"], A.single("Original"), owner);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit/check {objDbRef}/EDIT_CHECK_TEST=Original,Changed"));

		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_CHECK_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("Original");
	}

	[Test]
	public async ValueTask Test_Edit_Regex()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditRegex");
		var owner = (await Database.GetObjectNodeAsync(new(1))).AsPlayer;
		await Database.SetAttributeAsync(objDbRef, ["EDIT_REGEX_TEST"], A.single("foo123bar"), owner);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit/regexp {objDbRef}/EDIT_REGEX_TEST=\\\\d+,XXX"));

		var attr = Database.GetAttributeAsync(objDbRef, ["EDIT_REGEX_TEST"]);
		var attrList = await attr!.ToListAsync();
		await Assert.That(attrList.Last().Value.ToPlainText()).IsEqualTo("fooXXXbar");
	}

	[Test]
	public async ValueTask Test_Edit_NoMatch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "EditNoMatch");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@edit {objDbRef}/NONEXISTENT_EDIT_TEST=foo,bar"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EditNoMatchingAttributesFound), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask SetAttribute_UnclosedParen_StoresViaLenientParse()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetAttrUnclosed");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objName = obj.Known.Object().Name;

		// Missing closing ')' — lenient (ANTLR-recovery) command parsing should store the
		// best-effort value rather than silently dropping or rejecting the input.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&UNCLOSED_PAREN_ATTR {objDbRef}=ansi(hr,fun"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageEquals(msg, $"{objName}/UNCLOSED_PAREN_ATTR - Set.")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "UNCLOSED_PAREN_ATTR",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr.IsAttribute).IsTrue()
			.Because("lenient command parsing should store the ANTLR-recovered value");

		var storedValue = attr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("ansi(hr,fun")
			.Because("lenient mode stores verbatim source text, not ANTLR error-recovery annotations");
	}

	[Test]
	public async ValueTask SetAttribute_TildePrefix_UnclosedParen_ReportsParseFailure()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetAttrTildeStrict");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// The ~ prefix opts into strict parsing — an unclosed ')' must surface as #-1 PARSER FAILURE.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"~&UNCLOSED_TILDE_ATTR {objDbRef}=ansi(hr,fun"));

		await NotifyService
			.Received(1)
			.Notify(Arg.Any<long>(), Arg.Is<OneOf<MString, string>>(msg =>
				msg.Match(ms => ms.ToPlainText(), s => s).StartsWith("#-1 PARSER FAILURE")),
				Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());

		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "UNCLOSED_TILDE_ATTR",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(attr.IsAttribute).IsFalse()
			.Because("strict (~) parse mode should reject the command on syntax error");
	}
}
