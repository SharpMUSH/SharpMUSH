using System.ComponentModel;

namespace SharpMUSH.Configuration.Options;

public record LimitOptions(
	[property: SharpConfig(Name = "max_aliases", Description = "Maximum number of aliases a player can have")] uint MaxAliases,
	[property: SharpConfig(Name = "max_dbref", Description = "Maximum database reference number (highest object number)")] uint? MaxDbReference,
	[property: SharpConfig(Name = "max_attrs_per_obj", Description = "Maximum number of attributes per object")] uint MaxAttributesPerObj,
	[property: SharpConfig(Name = "max_logins", Description = "Maximum number of simultaneous logins allowed")] uint MaxLogins,
	[property: SharpConfig(Name = "max_guests", Description = "Maximum number of guest characters (-1 for unlimited)")] int MaxGuests,
	[property: SharpConfig(Name = "max_named_qregs", Description = "Maximum number of named Q-registers per player")] uint MaxNamedQRegisters,
	[property: SharpConfig(Name = "connect_fail_limit", Description = "Number of failed connects before IP is temporarily banned")] uint ConnectFailLimit,
	[property: SharpConfig(Name = "idle_timeout", Description = "Seconds before connected players are considered idle")] uint IdleTimeout,
	[property: SharpConfig(Name = "unconnected_idle_timeout", Description = "Seconds before unconnected sessions are dropped")] uint UnconnectedIdleTimeout,
	[property: SharpConfig(Name = "keepalive_timeout", Description = "Seconds between TCP keepalive probes")] uint KeepaliveTimeout,
	[property: SharpConfig(Name = "whisper_loudness", Description = "Distance whispers can be heard")] uint WhisperLoudness,
	[property: SharpConfig(Name = "starting_quota", Description = "Initial quota given to new players")] uint StartingQuota,
	[property: SharpConfig(Name = "starting_money", Description = "Initial money given to new players")] uint StartingMoney,
	[property: SharpConfig(Name = "paycheck", Description = "Money given to players when they connect")] uint Paycheck,
	[property: SharpConfig(Name = "guest_paycheck", Description = "Money given to guest players when they connect")] uint GuestPaycheck,
	[property: SharpConfig(Name = "max_pennies", Description = "Maximum money a player can have")] uint MaxPennies,
	[property: SharpConfig(Name = "max_guest_pennies", Description = "Maximum money a guest can have")] uint MaxGuestPennies,
	[property: SharpConfig(Name = "max_parents", Description = "Maximum inheritance chain depth for objects")] uint MaxParents,
	[property: SharpConfig(Name = "mail_limit", Description = "Maximum number of mail messages per player")] uint MailLimit,
	[property: SharpConfig(Name = "max_depth", Description = "Maximum nesting depth for function calls")] uint MaxDepth,
	[property: SharpConfig(Name = "player_queue_limit", Description = "Maximum commands a player can queue")] uint PlayerQueueLimit,
	[property: SharpConfig(Name = "queue_loss", Description = "Percentage chance commands are lost under heavy load")] uint QueueLoss,
	[property: SharpConfig(Name = "queue_chunk", Description = "Number of queued commands processed at once")] uint QueueChunk,
	[property: SharpConfig(Name = "function_recursion_limit", Description = "Maximum recursion depth for functions")] uint FunctionRecursionLimit,
	[property: SharpConfig(Name = "function_invocation_limit", Description = "Maximum number of function calls per command")] uint FunctionInvocationLimit,
	[property: SharpConfig(Name = "call_limit", Description = "Maximum nested function calls per evaluation")] uint CallLimit,
	[property: SharpConfig(Name = "player_name_len", Description = "Maximum length of player names")] uint PlayerNameLen,
	[property: SharpConfig(Name = "queue_entry_cpu_time", Description = "CPU time limit per queued command (seconds)")] uint QueueEntryCpuTime,
	[property: SharpConfig(Name = "use_quota", Description = "Enable quota system for object creation")] bool UseQuota,
	[property: SharpConfig(Name = "chunk_migrate", Description = "Number of objects to migrate per database optimization cycle")] uint ChunkMigrate
);