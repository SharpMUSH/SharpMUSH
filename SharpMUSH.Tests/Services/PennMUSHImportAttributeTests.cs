using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A PennMUSH object's attributes: their values, their tree, their flags and who set them, each read
/// back through the engine.
/// </summary>
public class PennMUSHImportAttributeTests
{
	/// <summary>
	/// Alice, a mortal, owns the widget. A wizard has locked one of its attributes away from her, and Bob
	/// set another.
	/// </summary>
	private static PennMUSHDatabase Fixture() => new()
	{
		Version = "Attribute Fixture",
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 3, Name = "Alice", Type = PennMUSHObjectType.Player, Owner = 3, Location = 0, Link = 0 },
			new PennMUSHObject { DBRef = 4, Name = "Bob", Type = PennMUSHObjectType.Player, Owner = 4, Location = 0, Link = 0 },
			new PennMUSHObject
			{
				DBRef = 10, Name = "Attribute Widget", Type = PennMUSHObjectType.Thing, Owner = 3, Location = 0, Link = 0,
				Attributes =
				[
					new PennMUSHAttribute { Name = "DESCRIBE", Owner = 3, Value = "A \x1b[31mred\x1b[0m widget.", Flags = ["no_command", "visual"] },
					new PennMUSHAttribute { Name = "FOO", Owner = 3, Value = "top", Flags = [] },
					new PennMUSHAttribute { Name = "FOO`BAR", Owner = 3, Value = "leaf", Flags = ["no_inherit"] },
					new PennMUSHAttribute { Name = "SECRET", Owner = 1, Value = "hush", Flags = ["wizard", "mortal_dark", "locked"] },
					new PennMUSHAttribute { Name = "ODD", Owner = 3, Value = "x", Flags = ["visual", "sparkly"] },
					new PennMUSHAttribute { Name = "ALIASED", Owner = 3, Value = "y", Flags = ["hidden", "private", "no_space"] },
					new PennMUSHAttribute { Name = "NOTE", Owner = 4, Value = "from Bob", Flags = [] },
					new PennMUSHAttribute { Name = "ORPHAN", Owner = 99, Value = "nobody's", Flags = [] }
				]
			}
		]
	};

	[Test]
	public async Task ValuesAndTheAttributeTreeArrive()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world);
		var widget = await WidgetAsync(world);

		var describe = await ReadAsync(world, widget, "DESCRIBE");
		await Assert.That(describe[^1].Value.ToPlainText()).IsEqualTo("A red widget.");

		var foo = await ReadAsync(world, widget, "FOO`BAR");
		await Assert.That(foo.Select(a => a.Value.ToPlainText())).IsEquivalentTo(["top", "leaf"]);
		await Assert.That(FlagNames(foo[0])).Contains("BRANCH");

		await Assert.That(result.AttributesConverted).IsEqualTo(8);
	}

	/// <summary>The dump's flags, however many and whichever they are, land on the attribute they came with.</summary>
	[Test]
	public async Task EachAttributeCarriesItsSourceFlags()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await ImportAsync(world);
		var widget = await WidgetAsync(world);

		await Assert.That(FlagNames((await ReadAsync(world, widget, "DESCRIBE"))[^1])).IsSupersetOf(["NO_COMMAND", "VISUAL"]);
		await Assert.That(FlagNames((await ReadAsync(world, widget, "FOO`BAR"))[^1])).Contains("NO_INHERIT");
		await Assert.That(FlagNames((await ReadAsync(world, widget, "NOTE"))[^1])).IsEmpty();
	}

	/// <summary>
	/// PennMUSH reads <c>hidden</c> as <c>mortal_dark</c>, <c>private</c> as <c>no_inherit</c> and
	/// <c>no_space</c> as <c>nospace</c> (src/atr_tab.c, attr_privs_set).
	/// </summary>
	[Test]
	public async Task PennMUSHsFlagAliasesAreItsFlags()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world);
		var widget = await WidgetAsync(world);

		await Assert.That(FlagNames((await ReadAsync(world, widget, "ALIASED"))[^1]))
			.IsSupersetOf(["MORTAL_DARK", "NO_INHERIT", "NOSPACE"]);
		await Assert.That(result.Warnings.Where(w => w.Contains("ALIASED"))).IsEmpty();
	}

	/// <summary>
	/// Loading a database is not a player typing <c>@set</c>. PennMUSH's loader applies an attribute's
	/// stored flags as they are, so a wizard-only attribute on a mortal's object stays wizard-only; judged
	/// as the object setting its own flags, the whole set was refused as unrecognised.
	/// </summary>
	[Test]
	public async Task PrivilegedFlagsOnAMortalsObjectArrive()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world);
		var widget = await WidgetAsync(world);

		await Assert.That(FlagNames((await ReadAsync(world, widget, "SECRET"))[^1]))
			.IsSupersetOf(["WIZARD", "MORTAL_DARK", "LOCKED"]);
		await Assert.That(result.Warnings.Where(w => w.Contains("SECRET"))).IsEmpty();
	}

	/// <summary>
	/// PennMUSH's string_to_atrflagsets fails a whole flag list on one name it does not know, so an
	/// attribute with a flag SharpMUSH lacks keeps its value, takes none of its flags, and is reported.
	/// </summary>
	[Test]
	public async Task AnUnrecognisedFlagIsReportedAndTheAttributeKeepsItsValue()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world);
		var widget = await WidgetAsync(world);

		var odd = (await ReadAsync(world, widget, "ODD"))[^1];
		await Assert.That(odd.Value.ToPlainText()).IsEqualTo("x");
		await Assert.That(FlagNames(odd)).DoesNotContain("VISUAL");
		await Assert.That(result.Warnings)
			.Contains(w => w.StartsWith("Failed to set flags [visual, sparkly] on attribute ODD of #10"));
	}

	/// <summary>
	/// The dump records who set each attribute (PennMUSH's AL_CREATOR). One that names no player here
	/// falls back to the object's owner.
	/// </summary>
	[Test]
	public async Task EachAttributeBelongsToWhoeverSetItInTheSource()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await ImportAsync(world);
		var widget = await WidgetAsync(world);

		await Assert.That(await OwnerNameAsync(world, widget, "NOTE")).IsEqualTo("Bob");
		await Assert.That(await OwnerNameAsync(world, widget, "SECRET")).IsEqualTo("One");
		await Assert.That(await OwnerNameAsync(world, widget, "DESCRIBE")).IsEqualTo("Alice");
		await Assert.That(await OwnerNameAsync(world, widget, "ORPHAN")).IsEqualTo("Alice");
	}

	/// <summary>
	/// The batched write is a Mediator write like any other: an attribute the engine has already cached
	/// reads as written afterwards, flags included.
	/// </summary>
	[Test]
	public async Task ABatchedWriteReachesAttributesTheEngineHasCached()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await ImportAsync(world);
		var widget = await WidgetAsync(world);
		var owner = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var visual = await world.Mediator.CreateStream(new GetAttributeFlagsQuery()).SingleAsync(f => f.Name == "visual");

		await Assert.That((await ReadAsync(world, widget, "NOTE"))[^1].Value.ToPlainText()).IsEqualTo("from Bob");

		await world.Mediator.Send(new SetAttributesCommand(widget.Object().DBRef,
		[
			new AttributeWrite(["NOTE"], MarkupText.Plain("rewritten"), owner, [visual]),
			new AttributeWrite(["NEW", "LEAF"], MarkupText.Plain("grown"), owner, [])
		]));

		var note = (await ReadAsync(world, widget, "NOTE"))[^1];
		await Assert.That(note.Value.ToPlainText()).IsEqualTo("rewritten");
		await Assert.That(FlagNames(note)).Contains("VISUAL");
		await Assert.That((await ReadAsync(world, widget, "NEW`LEAF")).Select(a => a.Value.ToPlainText()))
			.IsEquivalentTo(["", "grown"]);
	}

	private static async Task<ConversionResult> ImportAsync(IsolatedImportWorld world)
	{
		var result = await world.Converter.ConvertDatabaseAsync(Fixture());
		await Assert.That(result.Errors).IsEmpty();
		return result;
	}

	private static async Task<AnySharpObject> WidgetAsync(IsolatedImportWorld world)
	{
		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == "Attribute Widget")).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}

	private static async Task<SharpAttribute[]> ReadAsync(IsolatedImportWorld world, AnySharpObject obj, string path)
		=> await world.Mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, path.Split('`'))).ToArrayAsync();

	private static ISet<string> FlagNames(SharpAttribute attribute)
		=> attribute.Flags.Select(f => f.Name.ToUpperInvariant()).ToHashSet();

	private static async Task<string?> OwnerNameAsync(IsolatedImportWorld world, AnySharpObject obj, string path)
	{
		var owner = await (await ReadAsync(world, obj, path))[^1].Owner.WithCancellation(CancellationToken.None);
		return owner?.Object.Name;
	}
}
