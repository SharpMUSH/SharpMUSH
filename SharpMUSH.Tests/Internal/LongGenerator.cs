using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Internal;

public class LongGenerator
{
	[Test]
	public async ValueTask SimpleGeneratorTest()
	{
		var generatorObj = new NextUnoccupiedNumberGenerator(0);
		var generatorObj2 = new NextUnoccupiedNumberGenerator(0);

		var generator1 = generatorObj.Get();
		var generator2 = generatorObj2.Get();
		var generator3 = generatorObj.Get();

		var sequence1 = generator1.Take(10).ToArray();
		generatorObj.Release(5);
		var sequence2 = generator1.Take(2).ToArray();
		generatorObj2.Release(9);
		var sequence3 = generator2.Take(1);
		var sequence4 = generator3.Take(1).ToArray();

		foreach (var pair in sequence1.Zip([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]))
		{
			await Assert.That(pair.First).IsEqualTo(pair.Second);
		}

		foreach (var pair in sequence2.Zip([5, 10]))
		{
			await Assert.That(pair.First).IsEqualTo(pair.Second);
		}
		
		foreach (var pair in sequence3.Zip([9]))
		{
			await Assert.That(pair.First).IsEqualTo(pair.Second);
		}
		
		foreach (var pair in sequence4.Zip([11]))
		{
			await Assert.That(pair.First).IsEqualTo(pair.Second);
		}
	}
}