namespace SharpMUSH.Configuration.Options;

public record CostOptions(
	uint ObjectCost = 10,
	uint ExitCost = 1,
	uint LinkCost = 1,
	uint RoomCost = 10,
	uint QueueCost = 10,
	uint QuotaCost = 1,
	uint FindCost = 100
);