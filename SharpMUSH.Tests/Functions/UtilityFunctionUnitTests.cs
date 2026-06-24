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
	[Arguments("valid(attrname,TEST)", "1")]
	[Arguments("valid(attrname,123)", "0")]
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

	[Test]
	[Arguments("allof(1,1,1)", "1")]
	[Arguments("allof(1,0,1)", "0")]
	[Arguments("allof(0,0,0)", "0")]
	[Arguments("allof(add(1,1),sub(5,3))", "1")]
	public async Task AllOf_Evaluation(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("itext(test_string_ITEXT_case1)", "1")]
	[Arguments("itext(123)", "0")]
	[Arguments("itext(45.67)", "0")]
	[Arguments("itext(abc123)", "1")]
	public async Task IText_Validation(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
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

		await Parser.FunctionParse(MModule.single($"attrib_set({createResult},TEST_ATTR,test_value_CLONE)"));

		var cloneResult = (await Parser.FunctionParse(MModule.single($"clone({createResult},test_thing_CLONE_copy)")))?.Message!;
		var cloneDbRef = HelperFunctions.ParseDbRef(cloneResult.ToPlainText()).AsValue();

		await Assert.That(cloneDbRef.Number).IsNotEqualTo(originalDbRef.Number);

		var clone = await Mediator.Send(new GetObjectNodeQuery(cloneDbRef));
		await Assert.That(clone.IsThing).IsTrue();
		await Assert.That(clone.AsThing.Object.Name).IsEqualTo("test_thing_CLONE_copy");
	}

	[Test]
	public async Task TestLock_EvaluateLock()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(test_obj_TESTLOCK)")))?.Message!;

		var result = (await Parser.FunctionParse(MModule.single($"testlock({createResult},%#/%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Wipe_ClearAttributes()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(test_obj_WIPE)")))?.Message!;

		await Parser.FunctionParse(MModule.single($"attrib_set({createResult},ATTR1,value1)"));
		await Parser.FunctionParse(MModule.single($"attrib_set({createResult},ATTR2,value2)"));

		var wipeResult = (await Parser.FunctionParse(MModule.single($"wipe({createResult})")))?.Message!;

		await Assert.That(wipeResult.ToPlainText()).Contains("2");
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
