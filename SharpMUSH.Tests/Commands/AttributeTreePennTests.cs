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

/// <summary>
/// Tests for PennMUSH-style attribute tree behavior:
/// - Backtick-delimited attribute name validation
/// - Auto-creation of branch attributes
/// - Sequential clear (leaves before branches)
/// </summary>
public class AttributeTreePennTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>
	/// &amp;foo` me=baz -> should reject (trailing backtick is invalid)
	/// </summary>
	[Test]
	public async ValueTask SetAttribute_TrailingBacktick_ShouldReject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeTrail");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO` {objDbRef}=baz"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO`",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsFalse()
			.Because("attribute names ending with backtick should be rejected");
	}

	/// <summary>
	/// &amp;`bar me=baz -> should reject (leading backtick is invalid)
	/// </summary>
	[Test]
	public async ValueTask SetAttribute_LeadingBacktick_ShouldReject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeLead");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&`BAR {objDbRef}=baz"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "`BAR",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsFalse()
			.Because("attribute names starting with backtick should be rejected");
	}

	/// <summary>
	/// &amp;foo``bar me=baz -> should reject (double backtick is invalid)
	/// </summary>
	[Test]
	public async ValueTask SetAttribute_DoubleBacktick_ShouldReject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeDbl");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO``BAR {objDbRef}=baz"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO``BAR",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsFalse()
			.Because("attribute names with double backticks should be rejected");
	}

	/// <summary>
	/// &amp;foo`bar me=baz -> should succeed and auto-create FOO as a branch attribute
	/// </summary>
	[Test]
	public async ValueTask SetAttribute_BacktickTree_ShouldSucceedAndAutoCreateBranch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeAuto");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objName = obj.Known.Object().Name;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));

		var leafAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO`BAR",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(leafAttr.IsAttribute).IsTrue()
			.Because("setting foo`bar should succeed with valid backtick syntax");
	}

	/// <summary>
	/// After setting foo`bar, hasattr(me, foo) should return 1 (branch auto-created)
	/// </summary>
	[Test]
	public async ValueTask SetAttribute_BacktickTree_BranchAutoCreated()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeHas");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));

		var result = (await Parser.FunctionParse(MModule.single($"hasattr({objDbRef},FOO)")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("1")
			.Because("setting foo`bar should auto-create the FOO branch attribute");
	}

	/// <summary>
	/// Clearing a branch attribute that still has children should fail.
	/// </summary>
	[Test]
	public async ValueTask ClearAttribute_BranchWithChildren_ShouldFail()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeClrBr");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));

		// Try to clear the branch FOO while it still has children (no '=' = explicit clear)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {objDbRef}"));

		var branchAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(branchAttr.IsAttribute).IsTrue()
			.Because("clearing a branch with children should fail; branch should still exist");
	}

	/// <summary>
	/// Clearing a leaf attribute should succeed.
	/// </summary>
	[Test]
	public async ValueTask ClearAttribute_Leaf_ShouldSucceed()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeClrLf");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));

		// Clear the leaf FOO`BAR (no '=' = explicit clear)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}"));

		var leafAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO`BAR",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(leafAttr.IsAttribute).IsFalse()
			.Because("clearing a leaf attribute should succeed");
	}

	/// <summary>
	/// After clearing the leaf, the branch can then be cleared.
	/// </summary>
	[Test]
	public async ValueTask ClearAttribute_BranchAfterLeafCleared_ShouldSucceed()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TreeClrSeq");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));

		// Clear the leaf first (no '=' = explicit clear)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}"));

		// Now clear the branch (no '=' = explicit clear)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {objDbRef}"));

		var branchAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(branchAttr.IsAttribute).IsFalse()
			.Because("after clearing children, the branch should be clearable");
	}
}
