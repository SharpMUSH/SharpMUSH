namespace SharpMUSH.Configuration.Options;

public record CostOptions(
	[property: PennConfig(Name = "object_cost")] uint ObjectCost,
	[property: PennConfig(Name = "exit_cost")] uint ExitCost,
	[property: PennConfig(Name = "link_cost")] uint LinkCost,
	[property: PennConfig(Name = "room_cost")] uint RoomCost,
	[property: PennConfig(Name = "queue_cost")] uint QueueCost,
	[property: PennConfig(Name = "quota_cost")] uint QuotaCost,
	[property: PennConfig(Name = "find_cost")] uint FindCost
);