using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class BooleanExpressionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IBooleanExpressionParser BooleanParser => (IBooleanExpressionParser)WebAppFactoryArg.Services.GetService(typeof(IBooleanExpressionParser))!;
	
	private ISharpDatabase Database => (ISharpDatabase)WebAppFactoryArg.Services.GetService(typeof(ISharpDatabase))!;

	[Arguments("!#FALSE", true)]
	[Arguments("#TRUE", true)]
	[Arguments("(#TRUE)", true)]
	[Arguments("!#TRUE", false)]
	[Arguments("#FALSE", false)]
	[Arguments("(#FALSE)", false)]
	[Arguments("(#FALSE | #TRUE) & #TRUE", true)]
	[Arguments("(#FALSE | #TRUE) | #FALSE", true)]
	[Arguments("(#FALSE | #TRUE) & #FALSE", false)]
	[Arguments("#TRUE & #TRUE", true)]
	[Arguments("#TRUE | #TRUE", true)]
	[Arguments("#TRUE & !#FALSE", true)]
	[Arguments("#TRUE & #FALSE", false)]
	[Arguments("#FALSE & #TRUE", false)]
	[Arguments("#TRUE | #FALSE", true)]
	[Arguments("#FALSE | #TRUE", true)]
	[Arguments("#TRUE & #TRUE & #TRUE", true)]
	[Arguments("#TRUE | #TRUE | #TRUE", true)]
	[Arguments("#TRUE & !#FALSE | #TRUE", true)]
	[Test]
	public async Task SimpleExpressions(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database!.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	[Arguments("type^Player & #TRUE", true)]
	[Arguments("type^Player & #FALSE", false)]
	[Arguments("type^Player & !type^Player", false)]
	[Arguments("type^Thing", false)]
	[Arguments("type^Player", true)]
	[Test]
	public async Task TypeExpressions(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database!.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	[Arguments("type^Player", true)]
	[Arguments("type^Thing", true)]
	[Arguments("type^Room", true)]
	[Arguments("type^Exit", true)]
	[Arguments("type^Nonsense", false)]
	[Test]
	public async Task TypeValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database!.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}
}