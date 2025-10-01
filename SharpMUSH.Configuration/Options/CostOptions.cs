namespace SharpMUSH.Configuration.Options;

public record CostOptions(
	[property: PennConfig(Name = "object_cost", Description = "Cost in pennies to create a new object")] uint ObjectCost,
	[property: PennConfig(Name = "exit_cost", Description = "Cost in pennies to create a new exit")] uint ExitCost,
	[property: PennConfig(Name = "link_cost", Description = "Cost in pennies to link an exit")] uint LinkCost,
	[property: PennConfig(Name = "room_cost", Description = "Cost in pennies to create a new room")] uint RoomCost,
	[property: PennConfig(Name = "queue_cost", Description = "Cost in pennies to queue a command")] uint QueueCost,
	[property: PennConfig(Name = "quota_cost", Description = "Cost in pennies to buy quota")] uint QuotaCost,
	[property: PennConfig(Name = "find_cost", Description = "Cost in pennies to use the @find command")] uint FindCost
);