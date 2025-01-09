namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	[property: PennConfig(Name = "use_syslog")] bool UseSyslog,
	[property: PennConfig(Name = "log_commands")] bool LogCommands,
	[property: PennConfig(Name = "log_forces")] bool LogForces,
	[property: PennConfig(Name = "error_log")] string ErrorLog,
	[property: PennConfig(Name = "command_log")] string CommandLog,
	[property: PennConfig(Name = "wizard_log")] string WizardLog,
	[property: PennConfig(Name = "chkpt_log")] string CheckpointLog,
	[property: PennConfig(Name = "trace_log")] string TraceLog,
	[property: PennConfig(Name = "connect_log")] string ConnectLog,
	[property: PennConfig(Name = "mem_check")] bool MemoryCheck,
	[property: PennConfig(Name = "use_connlog")] bool UseConnLog
);