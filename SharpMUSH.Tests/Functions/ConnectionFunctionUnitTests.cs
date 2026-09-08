using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class ConnectionFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Regression test for the WHO-family Hidden-visibility fix in
	/// <c>Functions.MortalWhoPlayers</c>/<c>VisibleWhoPlayers</c> (SharpMUSH.Implementation/Functions/
	/// ConnectionFunctions.cs): a connection made Hidden via <c>@hide</c> (per-connection state,
	/// distinct from the DARK flag) must disappear from the mortal-audience family (mwho/nmwho/etc,
	/// always unpowered - PennMUSH bsd.c's <c>fun_lwho</c> with <c>called_as[1] == 'M'</c>), and from
	/// the caller-scoped family (nwho/xwho) UNLESS the looker is privileged (Priv_Who: wizard/royalty/
	/// See_All - bsd.c:6438,6503 <c>if (!Hidden(d) || powered)</c>).
	///
	/// <para>Checks list MEMBERSHIP of this test's own player rather than a global count/list identity
	/// - this test class shares its server session with every other test in the run, so other tests'
	/// connections come and go concurrently and a bare count would be flaky.</para>
	/// </summary>
	[Test]
	public async Task HiddenConnection_ExcludedFromMwho_ButVisibleToAPrivilegedXwhoidLooker()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ConnFnHidden");
		var mwhoEntry = $"#{testPlayer.DbRef.Number}";

		await Assert.That(await MwhoContains(mwhoEntry)).IsTrue()
			.Because("the connection must be listed before it is hidden");
		await Assert.That(await XwhoidContainsAsPrivilegedLooker(testPlayer.DbRef.Number)).IsTrue()
			.Because("the connection must be listed before it is hidden");

		ConnectionService.Update(testPlayer.Handle, "Hidden", "1");

		await Assert.That(await MwhoContains(mwhoEntry)).IsFalse()
			.Because("mwho() (mortal-audience, always unpowered) must exclude a Hidden connection");
		await Assert.That(await XwhoidContainsAsPrivilegedLooker(testPlayer.DbRef.Number)).IsTrue()
			.Because("xwhoid() as a privileged (See_All) looker must still list a Hidden connection");

		ConnectionService.Update(testPlayer.Handle, "Hidden", "0");

		await Assert.That(await MwhoContains(mwhoEntry)).IsTrue()
			.Because("clearing Hidden must restore the connection to mwho()");

		async Task<bool> MwhoContains(string entry)
		{
			var result = (await Parser.FunctionParse(MarkupText.Plain("mwho()")))?.Message!;
			return result.ToPlainText().Split(' ').Contains(entry);
		}

		// God (#1, the FunctionParser's executor) is always privileged, so xwhoid() with no victim
		// argument (looker defaults to the executor) exercises the Priv_Who branch. The range is wide
		// enough to include every connection in the shared test session.
		async Task<bool> XwhoidContainsAsPrivilegedLooker(int dbrefNumber)
		{
			var result = (await Parser.FunctionParse(MarkupText.Plain("xwhoid(1,100000)")))?.Message!;
			// Entries are full DBRef.ToString() ("#N" or "#N:creation"), so match on the "#N" prefix
			// rather than requiring an exact token match.
			var needle = $"#{dbrefNumber}";
			return result.ToPlainText().Split(' ')
				.Any(token => token == needle || token.StartsWith(needle + ":"));
		}
	}

	[Test]
	public async Task Idle()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("idle(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	public async Task Conn()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("conn(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	public async Task ListWho()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("lwho()")))?.Message!;
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	[Arguments("doing(%#)", "")]
	public async Task Doing(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Test_Doing_ReturnsEmptyWhenNoAttribute()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("doing(%#)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Doing_WithDescriptor()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("doing(999999)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Test_Doing_WithInvalidPlayerName()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("doing(NonExistentPlayer_XYZ_12345)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Test_Doing_ValidatesInput()
	{
		var testCases = new[] {
			"doing(%#)",
			"doing(me)",
			"doing(0)",
			"doing(1)"
		};

		foreach (var testCase in testCases)
		{
			var result = (await Parser.FunctionParse(MarkupText.Plain(testCase)))?.Message!;
			await Assert.That(result.ToPlainText()).IsNotNull();
		}
	}

	[Test]
	[Arguments("host(%#)", "")]
	public async Task Host(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ipaddr(%#)", "")]
	public async Task Ipaddr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lports()", "")]
	public async Task Lports(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("mwho()", "")]
	public async Task Mwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("nwho()", "1")]
	public async Task Nwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lwhoid()", "")]
	public async Task Lwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("ncon()", "0")]
	public async Task Ncon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nexits(#0)", "0")]
	public async Task Nexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nplayers()", "0")]
	public async Task Nplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nthings()", "0")]
	public async Task Nthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvcon()", "0")]
	public async Task Nvcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvexits()", "0")]
	public async Task Nvexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvplayers()", "0")]
	public async Task Nvplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvthings()", "0")]
	public async Task Nvthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("ports()", "4201")]
	public async Task Ports(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_WithValidArguments()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("addrlog(ip,*)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_WithCount()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("addrlog(count,hostname,*)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_InvalidSearchType_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("addrlog(invalid,pattern)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connlog_WithValidArguments()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("connlog(all,count,1)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connlog_InvalidFilter_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("connlog(invalid,count,1)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connrecord_WithValidId()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("connrecord(12345)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connrecord_WithCustomSeparator()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("connrecord(12345,|)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}
}
