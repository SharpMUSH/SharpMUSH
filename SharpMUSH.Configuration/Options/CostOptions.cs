namespace SharpMUSH.Configuration.Options;

public record CostOptions(
	[property: PennConfig(Name = "object_cost", Description = "Cost in pennies to create a new object", DefaultValue = "10")] uint ObjectCost,
	[property: PennConfig(Name = "exit_cost", Description = "Cost in pennies to create a new exit", DefaultValue = "1")] uint ExitCost,
	[property: PennConfig(Name = "link_cost", Description = "Cost in pennies to link an exit", DefaultValue = "1")] uint LinkCost,
	[property: PennConfig(Name = "room_cost", Description = "Cost in pennies to create a new room", DefaultValue = "10")] uint RoomCost,
	[property: PennConfig(Name = "queue_cost", Description = "Cost in pennies to queue a command", DefaultValue = "10")] uint QueueCost,
	[property: PennConfig(Name = "quota_cost", Description = "Cost in pennies to buy quota", DefaultValue = "1")] uint QuotaCost,
	[property: PennConfig(Name = "find_cost", Description = "Cost in pennies to use the @find command", DefaultValue = "100")] uint FindCost
);