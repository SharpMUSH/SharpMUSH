using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public partial class SpeechTransformationTests
{
	[Test]
	public async Task NestedForwardListDoesNotMakeAnAudibleThingAlive()
	{
		var reference = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "NestedForwardSpeaker");
		await Admin($"@set {reference}=AUDIBLE");
		await Admin($"&BRANCH`FORWARDLIST {reference}=#1");
		var node = await Node(reference);
		await Assert.That(await node.Object().LazyAllAttributes.Value.AnyAsync(attribute => attribute.Name == "FORWARDLIST")).IsTrue();
		await Assert.That(await node.IsAlive()).IsFalse();
	}

	[Test]
	[Arguments("none", true, false)]
	[Arguments("parent", true, false)]
	[Arguments("nested", true, false)]
	[Arguments("root", true, true)]
	[Arguments("empty", true, true)]
	[Arguments("root", false, false)]
	public async Task SharedAliveAndAutomaticNamesRequireALocalRootForwardList(string source, bool audible, bool alive)
	{
		var reference = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ForwardSpeaker");
		if (audible) await Flag(reference, "AUDIBLE");
		await Flag(reference, "DARK");
		var target = reference;
		if (source == "parent")
		{
			target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ForwardParent");
			await Admin($"@parent {reference}={target}");
		}
		// The list forwards to the object being set, not to #1: Can_Forward(thing, fwd) is checked at
		// set time (#1218) and nothing controls God, so "#1" is a value PennMUSH refuses. `what == who`
		// is controls()' first grant (src/predicat.c:387), and what this test needs is only that a
		// local root FORWARDLIST exists.
		if (source != "none")
			await Assert.That(await Attributes.SetAttributeAsync(await Node(new DBRef(1)), await Node(target), source == "nested" ? "BRANCH`FORWARDLIST" : "FORWARDLIST",
				source == "empty" ? MarkupText.Empty : MarkupText.Plain($"#{target.Number}")) is Success).IsTrue();
		var node = await Node(reference);
		if (source == "parent")
			await Assert.That((await node.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().DBRef).IsEqualTo(target);
		await Assert.That(await node.IsAlive()).IsEqualTo(alive);
		var names = new NameFormatter(Attributes, new FixedOptions(Options with { Command = Options.Command with { FullInvisibility = true } }));
		var expected = alive ? node.Object().Name : "Something";
		await Assert.That((await names.FormatAsync(node, NameContext.Speech)).ToPlainText()).IsEqualTo(expected);
		await Assert.That((await names.FormatAsync(node, NameContext.NoSpoof)).ToPlainText()).IsEqualTo(expected);
		await Assert.That((await names.FormatAsync(node, NameContext.Accented)).ToPlainText()).IsEqualTo(node.Object().Name);
		var visible = new NameFormatter(Attributes, new FixedOptions(Options with { Command = Options.Command with { FullInvisibility = false } }));
		await Assert.That((await visible.FormatAsync(node, NameContext.Speech)).ToPlainText()).IsEqualTo(node.Object().Name);
		await Admin($"@power {reference}=Can_Dark");
		await Assert.That((await names.FormatAsync(await Node(reference), NameContext.Speech)).ToPlainText()).IsEqualTo("Something");
		await Admin($"@set {reference}=!DARK");
		await Assert.That((await names.FormatAsync(await Node(reference), NameContext.Speech)).ToPlainText()).IsEqualTo(node.Object().Name);
	}
}
