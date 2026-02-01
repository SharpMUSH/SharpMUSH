namespace SharpMUSH.Configuration.Options;

public record LimitOptions(
	[property: SharpConfig(
		Name = "max_aliases",
		Category = "Limit",
		Description = "Maximum number of aliases a player can have",
		ValidationPattern = @"^\d+$",
		Group = "Players",
		Order = 1,
		Min = 1,
		Max = 1000)]
	uint MaxAliases,
	
	[property: SharpConfig(
		Name = "max_dbref",
		Category = "Limit",
		Description = "Maximum database reference number (highest object number)",
		ValidationPattern = @"^\d*$",
		Group = "Database",
		Order = 1,
		Min = 1000,
		Max = 2147483647)]
	uint? MaxDbReference,
	
	[property: SharpConfig(
		Name = "max_attrs_per_obj",
		Category = "Limit",
		Description = "Maximum number of attributes per object",
		ValidationPattern = @"^\d+$",
		Group = "Database",
		Order = 2,
		Min = 10,
		Max = 10000)]
	uint MaxAttributesPerObj,
	
	[property: SharpConfig(
		Name = "max_logins",
		Category = "Limit",
		Description = "Maximum number of simultaneous logins allowed",
		ValidationPattern = @"^\d+$",
		Group = "Connections",
		Order = 1,
		Min = 1,
		Max = 10000)]
	uint MaxLogins,
	
	[property: SharpConfig(
		Name = "max_guests",
		Category = "Limit",
		Description = "Maximum number of guest characters (-1 for unlimited)",
		ValidationPattern = @"^-1|\d+$",
		Group = "Connections",
		Order = 2,
		Min = -1,
		Max = 1000)]
	int MaxGuests,
	
	[property: SharpConfig(
		Name = "max_named_qregs",
		Category = "Limit",
		Description = "Maximum number of named Q-registers per player",
		ValidationPattern = @"^\d+$",
		Group = "Players",
		Order = 3,
		Min = 0,
		Max = 1000)]
	uint MaxNamedQRegisters,
	
	[property: SharpConfig(
		Name = "connect_fail_limit",
		Category = "Limit",
		Description = "Number of failed connects before IP is temporarily banned",
		ValidationPattern = @"^\d+$",
		Group = "Connections",
		Order = 3,
		Min = 1,
		Max = 100)]
	uint ConnectFailLimit,
	
	[property: SharpConfig(
		Name = "idle_timeout",
		Category = "Limit",
		Description = "Seconds before connected players are considered idle",
		ValidationPattern = @"^\d+$",
		Group = "Connections",
		Order = 4,
		Min = 60,
		Max = 86400)]
	uint IdleTimeout,
	
	[property: SharpConfig(
		Name = "unconnected_idle_timeout",
		Category = "Limit",
		Description = "Seconds before unconnected sessions are dropped",
		ValidationPattern = @"^\d+$",
		Group = "Connections",
		Order = 5,
		Min = 10,
		Max = 3600)]
	uint UnconnectedIdleTimeout,
	
	[property: SharpConfig(
		Name = "keepalive_timeout",
		Category = "Limit",
		Description = "Seconds between TCP keepalive probes",
		ValidationPattern = @"^\d+$",
		Group = "Connections",
		Order = 6,
		Min = 30,
		Max = 7200)]
	uint KeepaliveTimeout,
	
	[property: SharpConfig(
		Name = "whisper_loudness",
		Category = "Limit",
		Description = "Distance whispers can be heard",
		ValidationPattern = @"^\d+$",
		Group = "Communication",
		Order = 1,
		Min = 0,
		Max = 100)]
	uint WhisperLoudness,
	
	[property: SharpConfig(
		Name = "starting_quota",
		Category = "Limit",
		Description = "Initial quota given to new players",
		ValidationPattern = @"^\d+$",
		Group = "Economy",
		Order = 2,
		Min = 0,
		Max = 100000)]
	uint StartingQuota,
	
	[property: SharpConfig(
		Name = "starting_money",
		Category = "Limit",
		Description = "Initial money given to new players",
		ValidationPattern = @"^\d+$",
		Group = "Economy",
		Order = 3,
		Min = 0,
		Max = 1000000)]
	uint StartingMoney,
	
	[property: SharpConfig(
		Name = "paycheck",
		Category = "Limit",
		Description = "Money given to players when they connect",
		ValidationPattern = @"^\d+$",
		Group = "Economy",
		Order = 4,
		Min = 0,
		Max = 100000)]
	uint Paycheck,
	
	[property: SharpConfig(
		Name = "guest_paycheck",
		Category = "Limit",
		Description = "Money given to guest players when they connect",
		ValidationPattern = @"^\d+$",
		Group = "Economy",
		Order = 5,
		Min = 0,
		Max = 100000)]
	uint GuestPaycheck,
	
	[property: SharpConfig(
		Name = "max_pennies",
		Category = "Limit",
		Description = "Maximum money a player can have",
		ValidationPattern = @"^\d+$",
		Group = "Economy",
		Order = 6,
		Min = 0,
		Max = 2147483647)]
	uint MaxPennies,
	
	[property: SharpConfig(
		Name = "max_guest_pennies",
		Category = "Limit",
		Description = "Maximum money a guest can have",
		ValidationPattern = @"^\d+$",
		Group = "Economy",
		Order = 7,
		Min = 0,
		Max = 100000)]
	uint MaxGuestPennies,
	
	[property: SharpConfig(
		Name = "max_parents",
		Category = "Limit",
		Description = "Maximum inheritance chain depth for objects",
		ValidationPattern = @"^\d+$",
		Group = "Database",
		Order = 4,
		Min = 1,
		Max = 100)]
	uint MaxParents,
	
	[property: SharpConfig(
		Name = "mail_limit",
		Category = "Limit",
		Description = "Maximum number of mail messages per player",
		ValidationPattern = @"^\d+$",
		Group = "Players",
		Order = 4,
		Min = 0,
		Max = 100000)]
	uint MailLimit,
	
	[property: SharpConfig(
		Name = "max_depth",
		Category = "Limit",
		Description = "Maximum nesting depth for function calls",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 1,
		Min = 10,
		Max = 1000)]
	uint MaxDepth,
	
	[property: SharpConfig(
		Name = "player_queue_limit",
		Category = "Limit",
		Description = "Maximum commands a player can queue",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 5,
		Min = 1,
		Max = 10000)]
	uint PlayerQueueLimit,
	
	[property: SharpConfig(
		Name = "queue_loss",
		Category = "Limit",
		Description = "Percentage chance commands are lost under heavy load",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 6,
		Min = 0,
		Max = 100)]
	uint QueueLoss,
	
	[property: SharpConfig(
		Name = "queue_chunk",
		Category = "Limit",
		Description = "Number of queued commands processed at once",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 7,
		Min = 1,
		Max = 1000)]
	uint QueueChunk,
	
	[property: SharpConfig(
		Name = "function_recursion_limit",
		Category = "Limit",
		Description = "Maximum recursion depth for functions",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 2,
		Min = 10,
		Max = 10000)]
	uint FunctionRecursionLimit,
	
	[property: SharpConfig(
		Name = "function_invocation_limit",
		Category = "Limit",
		Description = "Maximum number of function calls per command",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 3,
		Min = 100,
		Max = 1000000)]
	uint FunctionInvocationLimit,
	
	[property: SharpConfig(
		Name = "call_limit",
		Category = "Limit",
		Description = "Maximum nested function calls per evaluation",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 4,
		Min = 10,
		Max = 10000)]
	uint CallLimit,
	
	[property: SharpConfig(
		Name = "player_name_len",
		Category = "Limit",
		Description = "Maximum length of player names",
		ValidationPattern = @"^\d+$",
		Group = "Players",
		Order = 2,
		Min = 3,
		Max = 100)]
	uint PlayerNameLen,
	
	[property: SharpConfig(
		Name = "queue_entry_cpu_time",
		Category = "Limit",
		Description = "CPU time limit per queued command (seconds)",
		ValidationPattern = @"^\d+$",
		Group = "Performance",
		Order = 8,
		Min = 1,
		Max = 300)]
	uint QueueEntryCpuTime,
	
	[property: SharpConfig(
		Name = "use_quota",
		Category = "Limit",
		Description = "Enable quota system for object creation",
		Group = "Economy",
		Order = 1)]
	bool UseQuota,
	
	[property: SharpConfig(
		Name = "chunk_migrate",
		Category = "Limit",
		Description = "Number of objects to migrate per database optimization cycle",
		ValidationPattern = @"^\d+$",
		Group = "Database",
		Order = 5,
		Min = 1,
		Max = 10000)]
	uint ChunkMigrate,
	
	[property: SharpConfig(
		Name = "max_attribute_value_length",
		Category = "Limit",
		Description = "Maximum byte length for attribute values",
		ValidationPattern = @"^\d+$",
		Group = "Database",
		Order = 3,
		Min = 100,
		Max = 1000000)]
	uint MaxAttributeValueLength
);
