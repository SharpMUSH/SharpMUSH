namespace SharpMUSH.Server.Strategy.ArangoDB;

public static class ArangoStartupStrategyProvider
{
	public static ArangoStartupStrategy GetStrategy()
	{
		var arangoConnStr = Environment.GetEnvironmentVariable("ARANGO_CONNECTION_STRING");

		if (!string.IsNullOrWhiteSpace(arangoConnStr))
		{
			return arangoConnStr.Contains(".sock")
				? new ArangoSocketStartupStrategy()
				: new ArangoKubernetesStartupStrategy(arangoConnStr);
		}
		
		var arangoTestConnStr = Environment.GetEnvironmentVariable("ARANGO_TEST_CONNECTION_STRING");
		
		if (!string.IsNullOrWhiteSpace(arangoTestConnStr))
		{
			return new ArangoKubernetesStartupStrategy(arangoTestConnStr);
		}

		return new ArangoTestContainerStartupStrategy();
	}
}