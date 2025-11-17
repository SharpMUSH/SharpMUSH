namespace SharpMUSH.Server.Strategy.ArangoDB;

public static class ArangoStartupStrategyProvider
{
	public static ArangoStartupStrategy GetStrategy()
	{
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
