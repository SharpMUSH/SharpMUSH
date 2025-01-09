﻿namespace SharpMUSH.Configuration.Options;

public record LimitOptions(
	[PennConfig(Name = "max_aliases")] uint MaxAliases,
	[PennConfig(Name = "max_dbref")] uint? MaxDbReference,
	[PennConfig(Name = "max_attrsperobj")] uint MaxAttributesPerObj,
	[PennConfig(Name = "max_logins")] uint MaxLogins,
	[PennConfig(Name = "max_guests")] int MaxGuests,
	[PennConfig(Name = "max_named_qregs")] uint MaxNamedQRegisters,
	[PennConfig(Name = "connect_fail_limit")] uint ConnectFailLimit,
	[PennConfig(Name = "idle_timeout")] uint IdleTimeout,
	[PennConfig(Name = "unconnected_idle_timeout")] uint UnconnectedIdleTimeout,
	[PennConfig(Name = "keepalive_timeout")] uint KeepaliveTimeout,
	[PennConfig(Name = "whisper_loudness")] uint WhisperLoudness,
	[PennConfig(Name = "starting_quota")] uint StartingQuota,
	[PennConfig(Name = "starting_money")] uint StartingMoney,
	[PennConfig(Name = "paycheck")] uint Paycheck,
	[PennConfig(Name = "guest_paycheck")] uint GuestPaycheck,
	[PennConfig(Name = "max_pennies")] uint MaxPennies,
	[PennConfig(Name = "max_guest_pennies")] uint MaxGuestPennies,
	[PennConfig(Name = "max_parents")] uint MaxParents,
	[PennConfig(Name = "mail_limit")] uint MailLimit,
	[PennConfig(Name = "max_depth")] uint MaxDepth,
	[PennConfig(Name = "player_queue_limit")] uint PlayerQueueLimit,
	[PennConfig(Name = "queue_loss")] uint QueueLoss,
	[PennConfig(Name = "queue_chunk")] uint QueueChunk,
	[PennConfig(Name = "function_recursion_limit")] uint FunctionRecursionLimit,
	[PennConfig(Name = "function_invocation_limit")] uint FunctionInvocationLimit,
	[PennConfig(Name = "call_limit")] uint CallLimit,
	[PennConfig(Name = "player_namelen")] uint PlayerNameLen,
	[PennConfig(Name = "queue_entry_cputime")] uint QueueEntryCpuTime,
	[PennConfig(Name = "use_quota")] bool UseQuota,
	[PennConfig(Name = "chunk_migrate")] uint ChunkMigrate
);