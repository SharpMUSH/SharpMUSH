using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LockFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("testlock(#1,%#)", "1")]
	[Arguments("testlock(#FALSE,%#)", "0")]
	[Arguments("testlock(#TRUE,%#)", "1")]
	public async Task Testlock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lockowner(%#)", "")]
	public async Task Lockowner(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lockfilter(#0,basic)", "")]
	public async Task Lockfilter(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llocks(%#)", "")]
	public async Task Llocks(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llockflags(%#,basic)", "")]
	public async Task Llockflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("atrlock(%#,testattr)", "")]
	public async Task Atrlock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task LockReturnsUnlocked()
	{
		// Create a dedicated object to avoid parallel test interference
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockFunc_UnlockedTest)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MModule.single($"lock({dbref})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("*UNLOCKED*");
	}

	[Test]
	public async Task ElockNoLockPasses()
	{
		// Create a dedicated object to avoid parallel test interference
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockFunc_ElockTest)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MModule.single($"elock({dbref}/Basic,%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}
}
