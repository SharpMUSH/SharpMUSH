using System.ComponentModel;

namespace SharpMUSH.Configuration.Options;

public record LimitOptions(
	[property: PennConfig(Name = "max_aliases", Description = "Maximum number of aliases a player can have", DefaultValue = "3")] uint MaxAliases,
	[property: PennConfig(Name = "max_dbref", Description = "Maximum database reference number (highest object number)", DefaultValue = null)] uint? MaxDbReference,
	[property: PennConfig(Name = "max_attrs_per_obj", Description = "Maximum number of attributes per object", DefaultValue = "2048")] uint MaxAttributesPerObj,
	[property: PennConfig(Name = "max_logins", Description = "Maximum number of simultaneous logins allowed", DefaultValue = "120")] uint MaxLogins,
	[property: PennConfig(Name = "max_guests", Description = "Maximum number of guest characters (-1 for unlimited)", DefaultValue = "-1")] int MaxGuests,
	[property: PennConfig(Name = "max_named_qregs", Description = "Maximum number of named Q-registers per player", DefaultValue = "100")] uint MaxNamedQRegisters,
	[property: PennConfig(Name = "connect_fail_limit", Description = "Number of failed connects before IP is temporarily banned", DefaultValue = "10")] uint ConnectFailLimit,
	[property: PennConfig(Name = "idle_timeout", Description = "Seconds before connected players are considered idle", DefaultValue = "0")] uint IdleTimeout,
	[property: PennConfig(Name = "unconnected_idle_timeout", Description = "Seconds before unconnected sessions are dropped", DefaultValue = "300")] uint UnconnectedIdleTimeout,
	[property: PennConfig(Name = "keepalive_timeout", Description = "Seconds between TCP keepalive probes", DefaultValue = "300")] uint KeepaliveTimeout,
	[property: PennConfig(Name = "whisper_loudness", Description = "Distance whispers can be heard", DefaultValue = "100")] uint WhisperLoudness,
	[property: PennConfig(Name = "starting_quota", Description = "Initial quota given to new players", DefaultValue = "20")] uint StartingQuota,
	[property: PennConfig(Name = "starting_money", Description = "Initial money given to new players", DefaultValue = "150")] uint StartingMoney,
	[property: PennConfig(Name = "paycheck", Description = "Money given to players when they connect", DefaultValue = "50")] uint Paycheck,
	[property: PennConfig(Name = "guest_paycheck", Description = "Money given to guest players when they connect", DefaultValue = "0")] uint GuestPaycheck,
	[property: PennConfig(Name = "max_pennies", Description = "Maximum money a player can have", DefaultValue = "1000000000")] uint MaxPennies,
	[property: PennConfig(Name = "max_guest_pennies", Description = "Maximum money a guest can have", DefaultValue = "100000000")] uint MaxGuestPennies,
	[property: PennConfig(Name = "max_parents", Description = "Maximum inheritance chain depth for objects", DefaultValue = "10")] uint MaxParents,
	[property: PennConfig(Name = "mail_limit", Description = "Maximum number of mail messages per player", DefaultValue = "300")] uint MailLimit,
	[property: PennConfig(Name = "max_depth", Description = "Maximum nesting depth for function calls", DefaultValue = "10")] uint MaxDepth,
	[property: PennConfig(Name = "player_queue_limit", Description = "Maximum commands a player can queue", DefaultValue = "100")] uint PlayerQueueLimit,
	[property: PennConfig(Name = "queue_loss", Description = "Percentage chance commands are lost under heavy load", DefaultValue = "63")] uint QueueLoss,
	[property: PennConfig(Name = "queue_chunk", Description = "Number of queued commands processed at once", DefaultValue = "3")] uint QueueChunk,
	[property: PennConfig(Name = "function_recursion_limit", Description = "Maximum recursion depth for functions", DefaultValue = "100")] uint FunctionRecursionLimit,
	[property: PennConfig(Name = "function_invocation_limit", Description = "Maximum number of function calls per command", DefaultValue = "100000")] uint FunctionInvocationLimit,
	[property: PennConfig(Name = "call_limit", Description = "Maximum nested function calls per evaluation", DefaultValue = "1000")] uint CallLimit,
	[property: PennConfig(Name = "player_name_len", Description = "Maximum length of player names", DefaultValue = "21")] uint PlayerNameLen,
	[property: PennConfig(Name = "queue_entry_cpu_time", Description = "CPU time limit per queued command (seconds)", DefaultValue = "1000")] uint QueueEntryCpuTime,
	[property: PennConfig(Name = "use_quota", Description = "Enable quota system for object creation", DefaultValue = "true")] bool UseQuota,
	[property: PennConfig(Name = "chunk_migrate", Description = "Number of objects to migrate per database optimization cycle", DefaultValue = "150")] uint ChunkMigrate
);