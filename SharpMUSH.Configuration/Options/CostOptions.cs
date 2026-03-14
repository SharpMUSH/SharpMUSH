namespace SharpMUSH.Configuration.Options;

public record CostOptions(
	[property: SharpConfig(
		Name = "object_cost",
		Category = "Cost",
		Description = "Cost in pennies to create a new object",
		ValidationPattern = @"^\d+$",
		Group = "Building Costs",
		Order = 1,
		Min = 0,
		Max = 100000)]
	uint ObjectCost,

	[property: SharpConfig(
		Name = "exit_cost",
		Category = "Cost",
		Description = "Cost in pennies to create a new exit",
		ValidationPattern = @"^\d+$",
		Group = "Building Costs",
		Order = 2,
		Min = 0,
		Max = 100000)]
	uint ExitCost,

	[property: SharpConfig(
		Name = "link_cost",
		Category = "Cost",
		Description = "Cost in pennies to link an exit",
		ValidationPattern = @"^\d+$",
		Group = "Building Costs",
		Order = 3,
		Min = 0,
		Max = 100000)]
	uint LinkCost,

	[property: SharpConfig(
		Name = "room_cost",
		Category = "Cost",
		Description = "Cost in pennies to create a new room",
		ValidationPattern = @"^\d+$",
		Group = "Building Costs",
		Order = 4,
		Min = 0,
		Max = 100000)]
	uint RoomCost,

	[property: SharpConfig(
		Name = "queue_cost",
		Category = "Cost",
		Description = "Cost in pennies to queue a command",
		ValidationPattern = @"^\d+$",
		Group = "Command Costs",
		Order = 1,
		Min = 0,
		Max = 1000)]
	uint QueueCost,

	[property: SharpConfig(
		Name = "quota_cost",
		Category = "Cost",
		Description = "Cost in pennies to buy quota",
		ValidationPattern = @"^\d+$",
		Group = "Command Costs",
		Order = 2,
		Min = 0,
		Max = 100000)]
	uint QuotaCost,

	[property: SharpConfig(
		Name = "find_cost",
		Category = "Cost",
		Description = "Cost in pennies to use the @find command",
		ValidationPattern = @"^\d+$",
		Group = "Command Costs",
		Order = 3,
		Min = 0,
		Max = 10000)]
	uint FindCost
);
