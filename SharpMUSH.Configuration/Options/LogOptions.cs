namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	[PennConfig(Name = "use_syslog")] bool UseSyslog,
	[PennConfig(Name = "log_commands")] bool LogCommands,
	[PennConfig(Name = "log_forces")] bool LogForces,
	[PennConfig(Name = "error_log")] string ErrorLog,
	[PennConfig(Name = "command_log")] string CommandLog,
	[PennConfig(Name = "wizard_log")] string WizardLog,
	[PennConfig(Name = "chkpt_log")] string CheckpointLog,
	[PennConfig(Name = "trace_log")] string TraceLog,
	[PennConfig(Name = "connect_log")] string ConnectLog,
	[PennConfig(Name = "memory_check")] bool MemoryCheck,
	[PennConfig(Name = "use_connlog")] bool UseConnLog
);