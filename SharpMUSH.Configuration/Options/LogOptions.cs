namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	[property: PennConfig(Name = "use_syslog", Description = "Send log messages to system syslog instead of files")] bool UseSyslog,
	[property: PennConfig(Name = "log_commands", Description = "Log all commands executed by players")] bool LogCommands,
	[property: PennConfig(Name = "log_forces", Description = "Log all @force commands and their results")] bool LogForces,
	[property: PennConfig(Name = "error_log", Description = "File path for error and warning messages")] string ErrorLog,
	[property: PennConfig(Name = "command_log", Description = "File path for command execution logs")] string CommandLog,
	[property: PennConfig(Name = "wizard_log", Description = "File path for wizard/admin activity logs")] string WizardLog,
	[property: PennConfig(Name = "chkpt_log", Description = "File path for database checkpoint logs")] string CheckpointLog,
	[property: PennConfig(Name = "trace_log", Description = "File path for detailed trace information")] string TraceLog,
	[property: PennConfig(Name = "connect_log", Description = "File path for player connection logs")] string ConnectLog,
	[property: PennConfig(Name = "mem_check", Description = "Enable memory usage checking and logging")] bool MemoryCheck,
	[property: PennConfig(Name = "use_connlog", Description = "Enable detailed connection logging")] bool UseConnLog
);