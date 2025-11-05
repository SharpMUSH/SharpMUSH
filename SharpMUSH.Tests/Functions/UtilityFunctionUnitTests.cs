using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class UtilityFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IPasswordService PasswordService => WebAppFactoryArg.Services.GetRequiredService<IPasswordService>(); 
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();


	// , DependsOn<SharpMUSH.Tests.Commands.RoomsAndMovementTests>
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
	[Skip("Not Yet Implemented")]
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
		// Should contain some known functions
		await Assert.That(functions).Contains("rand");
		await Assert.That(functions).Contains("add");
	}

	[Test]
	public async Task Functions_Wildcard()
	{
		var result = (await Parser.FunctionParse(MModule.single("functions(add*)")))?.Message!;
		var functions = result.ToPlainText();
		await Assert.That(functions).IsNotEmpty();
		// Should contain functions starting with "add"
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
	[Skip("Not Yet Implemented")]
	[Arguments("valid(attrname,TEST)", "1")]
	[Arguments("valid(attrname,123)", "0")]
	public async Task Valid(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("visible(%#,%#)", "1")]
	public async Task Visible(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
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
		// Should return a numeric value (time in milliseconds)
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
	public async Task AtAt(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("r(0)", "0")]
	public async Task R(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("recv()", "")]
	public async Task Recv(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("sent()", "")]
	public async Task Sent(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
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
		// rand() should return a value between 0 and 2^31-1
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
		// die(2,6) should return two space-separated dice rolls
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
		// die(5,6,0) should return only the sum
		var result = (await Parser.FunctionParse(MModule.single("die(5,6,0)")))?.Message!;
		var value = int.Parse(result.ToPlainText());
		await Assert.That(value).IsGreaterThanOrEqualTo(5);
		await Assert.That(value).IsLessThanOrEqualTo(30);
	}

	[Test]
	public async Task R_WithRegister()
	{
		// setq(A,test_value_r)[r(A)]
		var result = (await Parser.FunctionParse(MModule.single("setq(A,test_value_r)[r(A)]")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("test_value_r");
	}

	[Test]
	public async Task R_WithDefault()
	{
		// r(NONEXISTENT,default_value)
		var result = (await Parser.FunctionParse(MModule.single("r(NONEXISTENT,default_value)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("default_value");
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
		// slev() should return current stack depth
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
}
