using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class SoundFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// Penn soundex.1-soundex.4 (soundex hash type)
	[Test]
	[Arguments("soundex(a)", "A000")]
	[Arguments("soundex(fred)", "F630")]
	[Arguments("soundex(phred,soundex)", "F630")]
	[Arguments("soundex(afford)", "A163")]
	[Arguments("soundex(foobar)", "F160")]
	public async Task Soundex(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn soundslike.1-soundslike.5 (soundex comparison)
	[Test]
	[Arguments("soundslike(robin,robbyn)", "1")]
	[Arguments("soundslike(robin,roebuck)", "0")]
	[Arguments("soundslike(frick,frack)", "1")]
	[Arguments("soundslike(glacier,glazier)", "1")]
	[Arguments("soundslike(rutabega,rototiller,soundex)", "0")]
	[Arguments("soundslike(foobar,fubar)", "1")]
	public async Task Soundslike(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn soundex.5-soundex.7 (phone/metaphone hash type)
	[Test]
	[Arguments("soundex(fred,phone)", "BRD")]
	[Arguments("soundex(phred,phone)", "BRD")]
	[Arguments("soundex(afford,phone)", "ABRD")]
	public async Task SoundexPhone(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn soundex.8 (bad hash type)
	[Test]
	[Arguments("soundex(foo,bad hash)", "#-1 INVALID HASH TYPE")]
	[Arguments("soundslike(foo,bar,bad hash)", "#-1 INVALID HASH TYPE")]
	public async Task SoundexInvalidHash(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn soundslike.6-soundslike.10 (phone comparison)
	[Test]
	[Arguments("soundslike(robin,robbyn,phone)", "1")]
	[Arguments("soundslike(robin,roebuck,phone)", "0")]
	[Arguments("soundslike(frick,frack,phone)", "1")]
	[Arguments("soundslike(glacier,glazier,phone)", "1")]
	[Arguments("soundslike(rutabega,rototiller,phone)", "0")]
	public async Task SoundslikePhone(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
