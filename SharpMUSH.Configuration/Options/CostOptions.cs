namespace SharpMUSH.Configuration.Options;

public record CostOptions(
	[PennConfig(Name = "object_cost")] uint ObjectCost,
	[PennConfig(Name = "exit_cost")] uint ExitCost,
	[PennConfig(Name = "link_cost")] uint LinkCost,
	[PennConfig(Name = "room_cost")] uint RoomCost,
	[PennConfig(Name = "queue_cost")] uint QueueCost,
	[PennConfig(Name = "quota_cost")] uint QuotaCost,
	[PennConfig(Name = "find_cost")] uint FindCost
);