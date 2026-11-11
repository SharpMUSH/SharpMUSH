using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Services;

public class AttributeNameRewriterTests
{
	[Test]
	[Arguments("MERIT.LANGUAGE_()", "MERIT.LANGUAGE___")]
	[Arguments("_MERIT.ALLIES_(STREET CRIME)", "_MERIT.ALLIES__STREET_CRIME_")]
	[Arguments("`TREE", "_TREE")]
	[Arguments("TREE`", "TREE_")]
	[Arguments("A``B", "A`_B")]
	public async Task RenamesWhatSharpMushRefuses(string name, string renamed)
	{
		var names = AttributeNameRewriter.For([name, "VALID"]);

		await Assert.That(names.Renames).IsEquivalentTo(new Dictionary<string, string> { [name] = renamed });
		await Assert.That(AttributeNameRewriter.IsValid(renamed)).IsTrue();
		await Assert.That(names.Name("VALID")).IsEqualTo("VALID");
	}

	[Test]
	public async Task AClashGetsANumber()
	{
		var names = AttributeNameRewriter.For(["X()", "X__", "X[]"]);

		await Assert.That(names.Name("X()")).IsEqualTo("X___2");
		await Assert.That(names.Name("X[]")).IsEqualTo("X___3");
		await Assert.That(names.Name("X__")).IsEqualTo("X__");
	}

	[Test]
	public async Task RewritesWholeNamesInAnyCase()
	{
		var names = AttributeNameRewriter.For(["merit.language_()"]);

		var (text, changed) = names.Rewrite("[u(me/Merit.Language_())] [get(%0/xmerit.language_())] &merit.language_() me=1");

		await Assert.That(changed).IsTrue();
		await Assert.That(text).IsEqualTo("[u(me/MERIT.LANGUAGE___)] [get(%0/xmerit.language_())] &MERIT.LANGUAGE___ me=1");
		await Assert.That(names.Rewrite("nothing here")).IsEqualTo(("nothing here", false));
	}

	/// <summary>A name shaped like a function call, or with unbalanced parentheses, is renamed but code is left alone.</summary>
	[Test]
	[Arguments("ISDBREF()", "ISDBREF__")]
	[Arguments("FOO(", "FOO_")]
	public async Task CallShapedNamesAreNotFollowedInCode(string name, string renamed)
	{
		var names = AttributeNameRewriter.For([name]);

		await Assert.That(names.Name(name)).IsEqualTo(renamed);
		await Assert.That(names.Rewrite("[isdbref()] [foo(%0)]")).IsEqualTo(("[isdbref()] [foo(%0)]", false));
		await Assert.That(AttributeNameRewriter.Note(name, renamed)).EndsWith("change it by hand");
	}

	[Test]
	public async Task ARenameSkipsReservedNames()
		=> await Assert.That(AttributeNameRewriter.For(["DESC()"], ["DESC__"]).Name("DESC()")).IsEqualTo("DESC___2");

	/// <summary>A restriction an import needs replaces the configured one for that command, in any case.</summary>
	[Test]
	public async Task ImportedRestrictionsReplaceConfiguredOnes()
	{
		var now = new Dictionary<string, string[]> { ["addcom"] = ["wizard \"Use +chan"], ["@pemit"] = ["nogagged"] };

		var merged = PennMUSHDatabaseConverter.WithEntries(now, new Dictionary<string, string[]> { ["ADDCOM"] = ["nobody"] });

		await Assert.That(merged.Keys).IsEquivalentTo(["ADDCOM", "@pemit"]);
		await Assert.That(merged["ADDCOM"]).IsEquivalentTo(["nobody"]);
		await Assert.That(PennMUSHDatabaseConverter.WithEntries(merged, new Dictionary<string, string[]> { ["ADDCOM"] = ["nobody"] }))
			.IsSameReferenceAs(merged);
	}
}
