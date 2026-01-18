namespace SharpMUSH.Server.Strategy.ArangoDB;

public static class ArangoStartupStrategyProvider
{
	public static ArangoStartupStrategy GetStrategy()
	{
		// Check for test-specific connection string first (used by test infrastructure)
		var arangoTestConnStr = Environment.GetEnvironmentVariable("ARANGO_TEST_CONNECTION_STRING");
		if (!string.IsNullOrWhiteSpace(arangoTestConnStr))
		{
			return new ArangoKubernetesStartupStrategy(arangoTestConnStr);
		}

		// Check for production/Kubernetes connection string
		var arangoConnStr = Environment.GetEnvironmentVariable("ARANGO_CONNECTION_STRING");
		if (string.IsNullOrWhiteSpace(arangoConnStr))
		{
			return new ArangoTestContainerStartupStrategy();
		}
		else if (arangoConnStr.Contains(".sock"))
		{
			return new ArangoSocketStartupStrategy();
		}
		else {
			return new ArangoKubernetesStartupStrategy(arangoConnStr);
		}
	}
}
