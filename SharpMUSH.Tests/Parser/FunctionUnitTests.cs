namespace SharpMUSH.Tests.Parser;

public class FunctionUnitTests : BaseUnitTest
{
	[Test]
	[Arguments("strcat(strcat(),wi`th a[strcat(strcat(strcat(depth of 5)))])","wi`th adepth of 5")]
	// [Arguments("strcat(strcat(dog)", "strcat(dog")] // Currently Illegal according to the Parser. Fix maybe needed.
	[Arguments("strcat(foo\\,dog)", "foo,dog")]
	[Arguments("strcat(foo\\\\,dog)", "foo\\dog")]
	[Arguments("strcat(foo,-dog))", "foo-dog)")]
	[Arguments("\\t", "t")]
	[Arguments("add(1,5)","6")]
	[Arguments("add(1,add(2,3),add(2,2))", "10")]
	[Arguments("strcat(a,b,{c,def})", "abc,def")]
	[Arguments("add(1,2)[add(5,5)]", "310")]
	[Arguments("add(1,2)[add(5,5)]word()", "310word()")]
	public async Task Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		Console.WriteLine(string.Join("", result));

		if (expected is not null)
		{
			await Assert.That(result).IsEqualTo(expected);
		}
	}
}