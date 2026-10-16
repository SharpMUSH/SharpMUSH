using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class ArgumentSignatureRegistryTests
{
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SignatureUpdatesThroughAnAliasFollowItsConcreteScopedDefinition(bool local)
	{
		var registry = new UserDefinedFunctionService();
		DBRef? owner = local ? new DBRef(1, 1) : null;
		registry.Define(new UserDefinedFunction("target", new DBRef(2), "FN", 0, 32, true, null) { Owner = owner });
		registry.Alias("alias", "target", owner);
		registry.SetArgumentNames("alias", ["who"], owner);
		await Assert.That(string.Join(" ", registry.Resolve("target", owner)!.ArgumentNames)).IsEqualTo("who");
		await Assert.That(string.Join(" ", registry.Resolve("alias", owner)!.ArgumentNames)).IsEqualTo("who");
		if (local) await Assert.That(registry.Resolve("target")).IsNull();
	}
}
