using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The <c>lattr</c>/<c>nattr</c>/<c>xattr</c> family and its <c>reg</c>- and <c>-p</c> variants are
/// one PennMUSH function each: <c>fun_lattr</c> carries all eight L/X names and <c>fun_nattr</c> all
/// four N names (<c>src/function.c:529,619,703-707,830</c>), reading the variant out of
/// <c>called_as</c>. Written out twelve times here, they had drifted in two ways this pins.
/// </summary>
public class AttributeFamilyParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	/// <summary>
	/// PennMUSH reports this one as <c>called_as</c> — <c>"#-1 BAD ARGUMENT FORMAT TO %s"</c>
	/// (<c>src/fundb.c:225</c>). Five of the six regular-expression variants, and every plain
	/// variant, reported <c>GET</c> instead, because the copy they were made from did.
	/// </summary>
	[Test]
	[Arguments("lattr", "lattr(/x)")]
	[Arguments("lattrp", "lattrp(/x)")]
	[Arguments("nattr", "nattr(/x)")]
	[Arguments("nattrp", "nattrp(/x)")]
	[Arguments("xattr", "xattr(/x,1,1)")]
	[Arguments("xattrp", "xattrp(/x,1,1)")]
	[Arguments("reglattr", "reglattr(/x)")]
	[Arguments("reglattrp", "reglattrp(/x)")]
	[Arguments("regnattr", "regnattr(/x)")]
	[Arguments("regnattrp", "regnattrp(/x)")]
	[Arguments("regxattr", "regxattr(/x,1,1)")]
	[Arguments("regxattrp", "regxattrp(/x,1,1)")]
	public async Task EachVariantNamesItselfInItsArgumentFormatError(string name, string call)
		=> await Assert.That(await Eval(call))
			.IsEqualTo($"#-1 BAD ARGUMENT FORMAT TO {name.ToUpperInvariant()}")
			.Because($"PennMUSH formats this error with called_as, so {name} cannot report another function's name");

	/// <summary>
	/// <c>LATTR</c> is <c>{1, 2}</c> in PennMUSH's table and its second argument is the output
	/// delimiter, taken by the same <c>delim_check(…, 2, &amp;delim)</c> its <c>reg</c> twin uses
	/// (<c>src/fundb.c:177</c>). This copy declared <c>MaxArgs = 2</c> and then joined with a
	/// hardcoded space.
	/// </summary>
	[Test]
	[Arguments("lattr")]
	[Arguments("lattrp")]
	public async Task ListingHonoursItsOutputDelimiter(string function)
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await Eval($"[attrib_set(%!/D{uid}A,one)][attrib_set(%!/D{uid}B,two)]");

		await Assert.That(await Eval($"{function}(%!/D{uid}*,|)")).IsEqualTo($"D{uid}A|D{uid}B");
	}

	/// <summary>
	/// The parent form has to answer the same as the plain one for an object with no parent, which
	/// is the property that makes one shared body with a <c>checkParents</c> switch correct.
	/// </summary>
	[Test]
	public async Task ParentAndPlainVariantsAgreeOnAnObjectWithoutAParent()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var created = await Eval($"create(FamilyParity{uid})");
		await Eval($"[attrib_set({created}/P{uid}A,one)][attrib_set({created}/P{uid}B,two)]");

		await Assert.That(await Eval($"lattrp({created}/P{uid}*)")).IsEqualTo(await Eval($"lattr({created}/P{uid}*)"));
		await Assert.That(await Eval($"nattrp({created}/P{uid}*)")).IsEqualTo(await Eval($"nattr({created}/P{uid}*)"));
		await Assert.That(await Eval($"xattrp({created}/P{uid}*,1,1)")).IsEqualTo(await Eval($"xattr({created}/P{uid}*,1,1)"));
		await Assert.That(await Eval($"reglattrp({created}/P{uid}.*)")).IsEqualTo(await Eval($"reglattr({created}/P{uid}.*)"));
	}

	/// <summary>
	/// The <c>x</c> forms validate the start and the count before anything else
	/// (<c>src/fundb.c:157-171</c>).
	/// </summary>
	[Test]
	[Arguments("xattr(%!/*,x,1)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("xattr(%!/*,0,1)", "#-1 ARGUMENT OUT OF RANGE")]
	[Arguments("regxattrp(%!/.*,1,0)", "#-1 ARGUMENT OUT OF RANGE")]
	public async Task RangeFormsValidateTheirBounds(string call, string expected)
		=> await Assert.That(await Eval(call)).IsEqualTo(expected);

	/// <summary>
	/// PennMUSH's <c>fun_hasattr</c> serves all four names and reads both switches out of
	/// <c>called_as</c> (<c>src/fundb.c:215-260</c>): the parent walk and "has a value" rather than
	/// "exists". An attribute set to the empty string is the case that tells the pair apart.
	/// </summary>
	[Test]
	public async Task HasAttributeVariantsDifferOnlyInParentAndValue()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await Eval($"[attrib_set(%!/H{uid},)]");

		await Assert.That(await Eval($"hasattr(%!,H{uid})")).IsEqualTo("1");
		await Assert.That(await Eval($"hasattrp(%!,H{uid})")).IsEqualTo("1");
		await Assert.That(await Eval($"hasattrval(%!,H{uid})")).IsEqualTo("0");
		await Assert.That(await Eval($"hasattrpval(%!,H{uid})")).IsEqualTo("0");

		await Eval($"[attrib_set(%!/H{uid},filled)]");
		await Assert.That(await Eval($"hasattrval(%!,H{uid})")).IsEqualTo("1");
		await Assert.That(await Eval($"hasattrpval(%!,H{uid})")).IsEqualTo("1");
	}

	/// <summary>
	/// PennMUSH's <c>VAL</c> test is <c>!*AL_STR(a)</c>, plus a value of exactly one space when
	/// <c>empty_attrs</c> is off (<c>src/fundb.c:245-250</c>). Two spaces is a value. A blanket
	/// <c>IsNullOrWhiteSpace</c> answers 0 for it, and for a tab, and for a newline.
	/// </summary>
	[Test]
	[Arguments("%b%b", "two spaces")]
	[Arguments("%b%b%b", "three spaces")]
	public async Task AValueOfNothingButSpacesIsStillAValue(string value, string description)
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await Eval($"[attrib_set(%!/W{uid},{value})]");

		await Assert.That(await Eval($"hasattrval(%!,W{uid})")).IsEqualTo("1").Because(description);
		await Assert.That(await Eval($"hasattrpval(%!,W{uid})")).IsEqualTo("1").Because(description);
	}

	/// <summary>
	/// An attribute that exists but cannot be read is a refusal, not an absence: PennMUSH falls into
	/// <c>else if (a || !Can_Examine(...)) safe_str(T(e_perm))</c> (<c>src/fundb.c:254</c>). Telling
	/// a mortal <c>0</c> says the attribute is not there, which is a different answer.
	/// </summary>
	[Test]
	[Arguments("hasattr")]
	[Arguments("hasattrp")]
	[Arguments("hasattrval")]
	[Arguments("hasattrpval")]
	public async Task AnUnreadableAttributeIsRefusedRatherThanReportedAbsent(string function)
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(),
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>(),
			$"Hav{uid[..4]}");

		var owner = await Eval($"create(HavOwner{uid})");
		await Eval($"[attrib_set({owner}/S{uid},secret)]");
		await Eval($"[set({owner}/S{uid},mortal_dark)]");

		// Control: the wizard who set it can still read it, so a 0 below is the mortal's view and
		// not a failed set.
		await Assert.That(await Eval($"{function}({owner},S{uid})")).IsEqualTo("1");

		var asMortal = (await WebAppFactoryArg.CommandParser.CommandParse(
			mortal.Handle,
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>(),
			MarkupText.Plain($"think {function}({owner},S{uid})")))?.Message?.ToPlainText() ?? string.Empty;

		await Assert.That(asMortal).StartsWith("#-1")
			.Because("PennMUSH answers e_perm for an attribute it can see is there but may not read");
	}
}
