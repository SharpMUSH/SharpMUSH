using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class UtilityFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IPasswordService PasswordService => WebAppFactoryArg.Services.GetRequiredService<IPasswordService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();


	[Test]
	public async Task PCreate()
	{
		var result = (await Parser.FunctionParse(MModule.single("pcreate(John,SomePassword)")))?.Message?.ToString()!;

		var a = HelperFunctions.ParseDbRef(result).AsValue();
		var db = await Mediator.Send(new GetObjectNodeQuery(a));
		var player = db.AsPlayer;

		await Assert.That(PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash)).IsTrue();
		await Assert.That(PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash)).IsFalse();
	}

	[Test]
	public async Task Beep()
	{
		var result = (await Parser.FunctionParse(MModule.single("beep()")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("\a");
	}

	[Test]
	[Arguments("fn(testfunc)", "")]
	public async Task Fn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Functions_All()
	{
		var result = (await Parser.FunctionParse(MModule.single("functions()")))?.Message!;
		var functions = result.ToPlainText();
		await Assert.That(functions).IsNotEmpty();
		await Assert.That(functions).Contains("rand");
		await Assert.That(functions).Contains("add");
	}

	[Test]
	public async Task Functions_Wildcard()
	{
		var result = (await Parser.FunctionParse(MModule.single("functions(add*)")))?.Message!;
		var functions = result.ToPlainText();
		await Assert.That(functions).IsNotEmpty();
		await Assert.That(functions).Contains("add");
	}

	[Test]
	public async Task Functions_Exact()
	{
		var result = (await Parser.FunctionParse(MModule.single("functions(rand)")))?.Message!;
		var functions = result.ToPlainText();
		await Assert.That(functions).IsNotEmpty();
		await Assert.That(functions).Contains("rand");
	}

	[Test]
	/// <summary>
	/// Digits are valid attribute-name characters in PennMUSH - <c>attribute_names</c> lists
	/// '0'-'9' explicitly (<c>utils/gentables.c:80-81</c>) and <c>good_atr_name</c> imposes no
	/// leading-character rule beyond rejecting a leading or trailing backtick. So "123" is a valid
	/// attribute name, and the previous expectation of 0 was wrong.
	/// </summary>
	[Arguments("valid(attrname,TEST)", "1")]
	[Arguments("valid(attrname,123)", "1")]
	[Arguments("valid(attrname,`LEADING)", "0")]
	public async Task Valid(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("visible(%#,%#)", "1")]
	public async Task Visible(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("poll()", "")]
	public async Task Poll(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("benchmark(add(1,2),100)", "")]
	[Arguments("benchmark(sub(5,3),50)", "")]
	public async Task Benchmark(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var value = result.ToPlainText();
		await Assert.That(value).IsNotNull();
		await Assert.That(double.TryParse(value, out _)).IsTrue();
	}

	[Test]
	[Arguments("colors()", "")]
	public async Task Colors(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotEmpty();
	}

	[Test]
	[Arguments("isobjid(#1:0)", "1")]
	[Arguments("isobjid(#123:456789)", "1")]
	[Arguments("isobjid(notvalid)", "0")]
	[Arguments("isobjid(#1)", "0")]
	[Arguments("isobjid(#1:)", "0")]
	[Arguments("isobjid(1:0)", "0")]
	public async Task Isobjid(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("isint(123)", "1")]
	[Arguments("isint(-456)", "1")]
	[Arguments("isint(0)", "1")]
	[Arguments("isint(abc)", "0")]
	[Arguments("isint(12.34)", "0")]
	[Arguments("isint()", "0")]
	public async Task Isint(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("null()", "")]
	[Arguments("null(a)", "")]
	[Arguments("null(a,b,c)", "")]
	public async Task Null(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("s(Hello)", "Hello")]
	[Arguments("s(strcat\\(a\\,b\\))", "ab")]
	public async Task S(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("@@(test)", "")]
	[Arguments("@@()", "")]
	[Arguments("@@({a,b,c})", "")]
	public async Task AtAt(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("r(0)", "0")]
	public async Task R(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("recv()", "")]
	public async Task Recv(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("sent()", "")]
	public async Task Sent(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test, NotInParallel]
	public async Task SuggestFunction()
	{
		var dataService = WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();

		var suggestionData = new Library.ExpandedObjectData.SuggestionData(new Dictionary<string, HashSet<string>>
		{
			["test"] = new HashSet<string> { "apple", "application", "apply", "appreciate", "apricot", "banana", "grape" }
		});

		await dataService.SetExpandedServerDataAsync(suggestionData);

		// "aple" is a misspelling of apple
		var result1 = (await Parser.FunctionParse(MModule.single("suggest(test,aple)")))?.Message?.ToString();
		await Assert.That(result1).IsNotNull();
		await Assert.That(result1).Contains("apple");

		var result2 = (await Parser.FunctionParse(MModule.single("suggest(test,aple,|)")))?.Message?.ToString();
		await Assert.That(result2).IsNotNull();
		await Assert.That(result2).Contains("|");

		var result3 = (await Parser.FunctionParse(MModule.single("suggest(test,app,|,2)")))?.Message?.ToString();
		await Assert.That(result3).IsNotNull();
		var suggestions = result3!.Split('|');
		await Assert.That(suggestions.Length).IsLessThanOrEqualTo(2);

		var result4 = (await Parser.FunctionParse(MModule.single("suggest(nonexistent,word)")))?.Message?.ToString();
		await Assert.That(result4).IsEqualTo(string.Empty);
	}

	[Test]
	[Arguments("suggest(test)", "test")]
	public async Task Suggest(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async Task Rand_NoArgs()
	{
		var result = (await Parser.FunctionParse(MModule.single("rand()")))?.Message!;
		var value = int.Parse(result.ToPlainText());
		await Assert.That(value).IsGreaterThanOrEqualTo(0);
		await Assert.That(value).IsLessThan(int.MaxValue);
	}

	[Test]
	[Arguments("rand(10)", 0, 9)]
	[Arguments("rand(100)", 0, 99)]
	[Arguments("rand(1)", 0, 0)]
	public async Task Rand_OneArg(string str, int min, int max)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var value = int.Parse(result.ToPlainText());
		await Assert.That(value).IsGreaterThanOrEqualTo(min);
		await Assert.That(value).IsLessThanOrEqualTo(max);
	}

	[Test]
	[Arguments("rand(5,10)", 5, 10)]
	[Arguments("rand(0,5)", 0, 5)]
	[Arguments("rand(-5,5)", -5, 5)]
	public async Task Rand_TwoArgs(string str, int min, int max)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var value = int.Parse(result.ToPlainText());
		await Assert.That(value).IsGreaterThanOrEqualTo(min);
		await Assert.That(value).IsLessThanOrEqualTo(max);
	}

	[Test]
	public async Task Die_TwoDice()
	{
		var result = (await Parser.FunctionParse(MModule.single("die(2,6)")))?.Message!;
		var rolls = result.ToPlainText().Split(' ');
		await Assert.That(rolls.Length).IsEqualTo(2);
		foreach (var roll in rolls)
		{
			var value = int.Parse(roll);
			await Assert.That(value).IsGreaterThanOrEqualTo(1);
			await Assert.That(value).IsLessThanOrEqualTo(6);
		}
	}

	[Test]
	public async Task Die_ShowSum()
	{
		var result = (await Parser.FunctionParse(MModule.single("die(5,6,0)")))?.Message!;
		var value = int.Parse(result.ToPlainText());
		await Assert.That(value).IsGreaterThanOrEqualTo(5);
		await Assert.That(value).IsLessThanOrEqualTo(30);
	}

	[Test]
	public async Task R_WithRegister()
	{
		var result = (await Parser.FunctionParse(MModule.single("setq(A,test_value_r)[r(A)]")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("test_value_r");
	}

	[Test]
	public async Task R_TypeSelectorAndMissing()
	{
		// A missing q-register returns empty — the SECOND argument is a TYPE selector (per `help r`),
		// not a fallback default value.
		var empty = (await Parser.FunctionParse(MModule.single("r(NONEXISTENT)")))?.Message!;
		await Assert.That(empty.ToPlainText()).IsEqualTo("");

		// The explicit "qregisters" type reads setq/setr registers, same as the default.
		var explicitQ = (await Parser.FunctionParse(MModule.single("setq(A,qval)[r(A,qregisters)]")))?.Message!;
		await Assert.That(explicitQ.ToPlainText()).IsEqualTo("qval");

		// An unrecognized type is an error (it is NOT treated as a default value any more).
		var badType = (await Parser.FunctionParse(MModule.single("r(NONEXISTENT,default_value)")))?.Message!;
		await Assert.That(badType.ToPlainText()).StartsWith("#-1");

		// The type accepts unambiguous prefixes: "q" resolves to "qregisters".
		var prefixQ = (await Parser.FunctionParse(MModule.single("setq(B,bval)[r(B,q)]")))?.Message!;
		await Assert.That(prefixQ.ToPlainText()).IsEqualTo("bval");
	}

	[Test]
	public async Task Registers_Count()
	{
		var result = (await Parser.FunctionParse(MModule.single("setq(A,1)[setq(B,2)][registers()]")))?.Message!;
		await Assert.That(int.Parse(result.ToPlainText())).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task Registers_List()
	{
		var result = (await Parser.FunctionParse(MModule.single("setq(TEST1,val1)[setq(TEST2,val2)][registers(list)]")))?.Message!;
		var list = result.ToPlainText();
		await Assert.That(list).Contains("TEST1");
		await Assert.That(list).Contains("TEST2");
	}

	[Test]
	public async Task SLev_CheckDepth()
	{
		var result = (await Parser.FunctionParse(MModule.single("slev()")))?.Message!;
		var depth = int.Parse(result.ToPlainText());
		await Assert.That(depth).IsGreaterThanOrEqualTo(0);
	}

	/// <summary>
	/// <c>allof(&lt;expr&gt;[, ...], &lt;osep&gt;)</c> - "Evaluates every &lt;expr&gt; argument
	/// (including side-effects) and returns the results of those which are true, in a list
	/// separated by &lt;osep&gt;. The output separator argument is required"
	/// (<c>help ALLOF()</c>). It is NOT a boolean AND, which is what these cases previously
	/// asserted: with the last argument consumed as the separator, <c>allof(1,1,1)</c> is two
	/// true values joined by "1".
	/// </summary>
	[Test]
	[Arguments("allof(1,1,1)", "111")]
	[Arguments("allof(1,0,1)", "1")]
	[Arguments("allof(0,0,0)", "")]
	[Arguments("allof(add(1,1),sub(5,3))", "2")]
	[Arguments("allof(#-1,#101,#2970,,#-3,0,#319,null(x),|)", "#101|#2970|#319")]
	public async Task AllOf_Evaluation(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>itext(&lt;n&gt;)</c> returns the <c>##</c> of the &lt;n&gt;th enclosing
	/// <c>iter()</c>/<c>@dolist</c>, where &lt;n&gt; is a number or "L" (<c>help ITEXT()</c>). It
	/// does not validate a string and return 1/0, which is what these cases previously asserted -
	/// they compared the function's OUTPUT against a boolean.
	/// </summary>
	[Test]
	[Arguments("itext(test_string_ITEXT_case1)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("itext(abc123)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("itext(45.67)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("itext(123)", "#-1 REGISTER OUT OF RANGE")]
	public async Task IText_Validation(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// Nesting semantics, asserted against the worked examples in <c>help ITEXT2</c>: level 0 is the
	/// CURRENT (innermost) iteration, level 1 the one it is nested in, and "L" — equivalently
	/// <c>itext(ilev())</c> — the outermost. <c>%i&lt;n&gt;</c> is documented as an alias for
	/// <c>itext(&lt;n&gt;)</c>, so the two must agree.
	///
	/// <para>They did not. <c>itext()</c>/<c>inum()</c> reverse-indexed a stack that already
	/// enumerates innermost-first, so <c>itext(0)</c> answered the OUTERMOST iteration and
	/// <c>itext(ilev())</c> the innermost — the exact opposite of both the helpfile and
	/// <c>%i&lt;n&gt;</c>, which indexes the same stack directly.</para>
	/// </summary>
	[Test]
	[Arguments("iter(red blue green,iter(fish shoe,%i1:%i0))",
		"red:fish red:shoe blue:fish blue:shoe green:fish green:shoe")]
	[Arguments("iter(red blue green,iter(fish shoe,[itext(1)]:[itext(0)]))",
		"red:fish red:shoe blue:fish blue:shoe green:fish green:shoe")]
	[Arguments("iter(red blue green,iter(fish shoe,inum(0):[itext(0)]))",
		"1:fish 2:shoe 1:fish 2:shoe 1:fish 2:shoe")]
	[Arguments("iter(red blue green,iter(fish shoe,inum(ilev()):[itext(1)]))",
		"1:red 1:red 2:blue 2:blue 3:green 3:green")]
	[Arguments("iter(red blue green,iter(fish shoe,%iL))", "red red blue blue green green")]
	[Arguments("iter(red blue green,iter(fish shoe,[itext(L)]))", "red red blue blue green green")]
	// ## and #@ are rewritten to %iL by iter()/foreach()/@dolist, so a nested one names the
	// OUTERMOST loop — help ITEXT2's first worked example.
	[Arguments("iter(red blue green,iter(fish shoe,##))", "red red blue blue green green")]
	[Arguments("iter(a b,ilev())", "0 0")]
	[Arguments("iter(a,iter(b,ilev()))", "1")]
	public async Task ITextAndINum_CountFromTheInnermostIteration(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>%iL</c> hands back the register itself, colour and all. It used to interpolate the register
	/// into a string, which flattens an <c>MString</c> to its plain text, so <c>iter(ansi(...),%iL)</c>
	/// lost the colour that <c>%i0</c> — the same register, read the other way — kept.
	/// </summary>
	[Test]
	[Arguments("%iL")]
	[Arguments("%i0")]
	[Arguments("[itext(L)]")]
	public async Task TheOutermostIterationRegister_KeepsItsMarkup(string substitution)
	{
		var result = (await Parser.FunctionParse(MModule.single($"iter([ansi(+red,coloured)],{substitution})")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("coloured");
		await Assert.That(result.ToString()).Contains("\u001b[")
			.Because("the register carries the colour, and reading it must not flatten it to plain text");
	}

	[Test]
	public async Task Dig_CreateRoom()
	{
		var result = (await Parser.FunctionParse(MModule.single("dig(test_room_DIG_case1)")))?.Message!;
		var resultStr = result.ToPlainText();

		await Assert.That(resultStr).StartsWith("#");

		var dbRef = HelperFunctions.ParseDbRef(resultStr).AsValue();
		var room = await Mediator.Send(new GetObjectNodeQuery(dbRef));
		await Assert.That(room.IsRoom).IsTrue();
		await Assert.That(room.AsRoom.Object.Name).IsEqualTo("test_room_DIG_case1");
	}

	[Test]
	public async Task Open_CreateExit()
	{
		var result = (await Parser.FunctionParse(MModule.single("open(test_exit_OPEN_case1;te1)")))?.Message!;
		var resultStr = result.ToPlainText();

		await Assert.That(resultStr).StartsWith("#");

		var dbRef = HelperFunctions.ParseDbRef(resultStr).AsValue();
		var exit = await Mediator.Send(new GetObjectNodeQuery(dbRef));
		await Assert.That(exit.IsExit).IsTrue();
		await Assert.That(exit.AsExit.Object.Name).IsEqualTo("test_exit_OPEN_case1");
	}

	[Test]
	public async Task Clone_CopyObject()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(test_thing_CLONE_original)")))?.Message!;
		var originalDbRef = HelperFunctions.ParseDbRef(createResult.ToPlainText()).AsValue();

		// attrib_set(<object>/<attrib>, <value>) - see Wipe_ClearAttributes. In the old form this
		// line set nothing at all, so the attribute it was meant to give the clone never existed.
		await Parser.FunctionParse(MModule.single($"attrib_set({createResult}/TEST_ATTR,test_value_CLONE)"));

		var cloneResult = (await Parser.FunctionParse(MModule.single($"clone({createResult},test_thing_CLONE_copy)")))?.Message!;
		var cloneDbRef = HelperFunctions.ParseDbRef(cloneResult.ToPlainText()).AsValue();

		await Assert.That(cloneDbRef.Number).IsNotEqualTo(originalDbRef.Number);

		var clone = await Mediator.Send(new GetObjectNodeQuery(cloneDbRef));
		await Assert.That(clone.IsThing).IsTrue();
		await Assert.That(clone.AsThing.Object.Name).IsEqualTo("test_thing_CLONE_copy");

		var clonedAttr = (await Parser.FunctionParse(MModule.single($"get({cloneResult}/TEST_ATTR)")))?.Message!;
		await Assert.That(clonedAttr.ToPlainText()).IsEqualTo("test_value_CLONE")
			.Because("clone() copies the original's attributes - otherwise setting one here proves nothing");
	}

	/// <summary>
	/// <c>testlock(&lt;key&gt;, &lt;victim&gt;)</c> - the LOCK comes first (<c>help TESTLOCK()</c>).
	/// This previously passed the object as the key and <c>%#/%#</c> as the victim, which is not a
	/// victim at all; <c>#-1 NO MATCH</c> was the correct answer to the question it actually asked.
	/// </summary>
	[Test]
	public async Task TestLock_EvaluateLock()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(test_obj_TESTLOCK)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var passes = (await Parser.FunctionParse(MModule.single("testlock(FLAG^WIZARD,#1)")))?.Message!;
		await Assert.That(passes.ToPlainText()).IsEqualTo("1")
			.Because("#1 is a wizard, so it passes FLAG^WIZARD");

		var fails = (await Parser.FunctionParse(MModule.single($"testlock(FLAG^WIZARD,{dbref})")))?.Message!;
		await Assert.That(fails.ToPlainText()).IsEqualTo("0")
			.Because("a freshly created thing is not a wizard - without this the assertion above proves nothing");
	}

	/// <summary>
	/// A bare dbref is an object lock: only that object passes it. SharpMUSH returns 1 for every
	/// victim, so a lock written as <c>@lock &lt;obj&gt;=#123</c> admits everyone.
	///
	/// <para>Measured alongside <see cref="TestLock_EvaluateLock"/>, which shows the evaluator
	/// itself works - <c>testlock(FLAG^WIZARD, ...)</c> correctly returns 1 for #1 and 0 for a new
	/// thing. Only the bare-dbref key form is broken, so this is boolexp parsing rather than lock
	/// evaluation. Skipped rather than deleted so the gap stays visible and named; fixing it means
	/// touching the lock parser and belongs in its own change.</para>
	/// </summary>
	[Test]
	[Skip("Bare-dbref lock keys always pass - see the doc comment. Tracked separately.")]
	public async Task TestLock_BareDbrefKey_OnlyThatObjectPasses()
	{
		var subject = (await Parser.FunctionParse(MModule.single("create(test_obj_TESTLOCK_subject)")))?.Message!.ToPlainText()!;
		var other = (await Parser.FunctionParse(MModule.single("create(test_obj_TESTLOCK_other)")))?.Message!.ToPlainText()!;

		var passes = (await Parser.FunctionParse(MModule.single($"testlock({subject},{subject})")))?.Message!;
		await Assert.That(passes.ToPlainText()).IsEqualTo("1");

		var fails = (await Parser.FunctionParse(MModule.single($"testlock({subject},{other})")))?.Message!;
		await Assert.That(fails.ToPlainText()).IsEqualTo("0")
			.Because("an object lock names exactly one object");
	}

	[Test]
	public async Task Wipe_ClearAttributes()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(test_obj_WIPE)")))?.Message!;

		// attrib_set(<object>/<attrib>[, <value>]) - the object and attribute are ONE argument
		// (help ATTRIB_SET()). The previous form, attrib_set(<obj>,ATTR1,value1), returned
		// "#-1 BAD ARGUMENT FORMAT TO ATTRIB_SET" and set nothing, which is why the original
		// assertion saw a wipe count of zero: there was never anything on the object to wipe.
		await Parser.FunctionParse(MModule.single($"attrib_set({createResult}/ATTR1,value1)"));
		await Parser.FunctionParse(MModule.single($"attrib_set({createResult}/ATTR2,value2)"));

		// Control: the attributes are readable before the wipe, so a passing assertion below
		// cannot be the attrib_set calls having silently done nothing.
		var before = (await Parser.FunctionParse(MModule.single($"get({createResult}/ATTR1)")))?.Message!;
		await Assert.That(before.ToPlainText()).IsEqualTo("value1");

		var wipeResult = (await Parser.FunctionParse(MModule.single($"wipe({createResult})")))?.Message!;
		await Assert.That(wipeResult.ToPlainText()).IsEqualTo(string.Empty)
			.Because("PennMUSH's wipe() returns nothing at all");

		var after1 = (await Parser.FunctionParse(MModule.single($"get({createResult}/ATTR1)")))?.Message!;
		var after2 = (await Parser.FunctionParse(MModule.single($"get({createResult}/ATTR2)")))?.Message!;
		await Assert.That(after1.ToPlainText()).IsEmpty();
		await Assert.That(after2.ToPlainText()).IsEmpty();
	}

	[Test]
	public async Task ANSI_NamedColor()
	{
		// named color from colors.json
		var result = (await Parser.FunctionParse(MModule.single("ansi(+red,test)")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEqualTo("test");

		var fullText = result.ToString();
		await Assert.That(fullText).Contains("test");
		await Assert.That(fullText).Contains("\u001b[");
	}

	[Test]
	public async Task ANSI_NamedBackgroundColor()
	{
		var result = (await Parser.FunctionParse(MModule.single("ansi(/+blue,test)")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEqualTo("test");

		var fullText = result.ToString();
		await Assert.That(fullText).Contains("test");
		await Assert.That(fullText).Contains("\u001b[");
	}

	[Test]
	public async Task ANSI_XtermColor()
	{
		// xterm color (0-255)
		var result = (await Parser.FunctionParse(MModule.single("ansi(196,test)")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEqualTo("test");

		var fullText = result.ToString();
		await Assert.That(fullText).Contains("test");
		await Assert.That(fullText).Contains("\u001b[");
	}

	[Test]
	public async Task ANSI_XtermWithPrefix()
	{
		// +xterm prefix format
		var result = (await Parser.FunctionParse(MModule.single("ansi(+xterm196,test)")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEqualTo("test");

		var fullText = result.ToString();
		await Assert.That(fullText).Contains("test");
		await Assert.That(fullText).Contains("\u001b[");
	}

	[Test]
	public async Task ANSI_RGBFormat()
	{
		// RGB format <r g b>
		var result = (await Parser.FunctionParse(MModule.single("ansi(<255 0 0>,test)")))?.Message!;
		var plainText = result.ToPlainText();

		await Assert.That(plainText).IsEqualTo("test");

		var fullText = result.ToString();
		await Assert.That(fullText).Contains("test");
		await Assert.That(fullText).Contains("\u001b[");
		await Assert.That(fullText).Contains("38;2;255;0;0"); // RGB red color
	}


}
