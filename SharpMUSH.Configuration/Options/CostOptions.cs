namespace SharpMUSH.Configuration.Options;

public record CostOptions(
	[property: SharpConfig(Name = "object_cost", Description = "Cost in pennies to create a new object")] uint ObjectCost,
	[property: SharpConfig(Name = "exit_cost", Description = "Cost in pennies to create a new exit")] uint ExitCost,
	[property: SharpConfig(Name = "link_cost", Description = "Cost in pennies to link an exit")] uint LinkCost,
	[property: SharpConfig(Name = "room_cost", Description = "Cost in pennies to create a new room")] uint RoomCost,
	[property: SharpConfig(Name = "queue_cost", Description = "Cost in pennies to queue a command")] uint QueueCost,
	[property: SharpConfig(Name = "quota_cost", Description = "Cost in pennies to buy quota")] uint QuotaCost,
	[property: SharpConfig(Name = "find_cost", Description = "Cost in pennies to use the @find command")] uint FindCost
);