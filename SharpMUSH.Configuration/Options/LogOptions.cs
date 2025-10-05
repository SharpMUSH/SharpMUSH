namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	[property: SharpConfig(Name = "use_syslog", Description = "Send log messages to system syslog instead of files")] bool UseSyslog,
	[property: SharpConfig(Name = "log_commands", Description = "Log all commands executed by players")] bool LogCommands,
	[property: SharpConfig(Name = "log_forces", Description = "Log all @force commands and their results")] bool LogForces,
	[property: SharpConfig(Name = "error_log", Description = "File path for error and warning messages")] string ErrorLog,
	[property: SharpConfig(Name = "command_log", Description = "File path for command execution logs")] string CommandLog,
	[property: SharpConfig(Name = "wizard_log", Description = "File path for wizard/admin activity logs")] string WizardLog,
	[property: SharpConfig(Name = "chkpt_log", Description = "File path for database checkpoint logs")] string CheckpointLog,
	[property: SharpConfig(Name = "trace_log", Description = "File path for detailed trace information")] string TraceLog,
	[property: SharpConfig(Name = "connect_log", Description = "File path for player connection logs")] string ConnectLog,
	[property: SharpConfig(Name = "mem_check", Description = "Enable memory usage checking and logging")] bool MemoryCheck,
	[property: SharpConfig(Name = "use_connlog", Description = "Enable detailed connection logging")] bool UseConnLog
);