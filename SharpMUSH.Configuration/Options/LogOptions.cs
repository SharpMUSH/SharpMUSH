namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	[property: SharpConfig(
		Name = "use_syslog",
		Category = "Log",
		Description = "Send log messages to system syslog instead of files",
		Group = "Log Destination",
		Order = 1)]
	bool UseSyslog,

	[property: SharpConfig(
		Name = "log_commands",
		Category = "Log",
		Description = "Log all commands executed by players",
		Group = "Log Content",
		Order = 1)]
	bool LogCommands,

	[property: SharpConfig(
		Name = "log_forces",
		Category = "Log",
		Description = "Log all @force commands and their results",
		Group = "Log Content",
		Order = 2)]
	bool LogForces,

	[property: SharpConfig(
		Name = "error_log",
		Category = "Log",
		Description = "File path for error and warning messages",
		Group = "Log Files",
		Order = 1)]
	string ErrorLog,

	[property: SharpConfig(
		Name = "command_log",
		Category = "Log",
		Description = "File path for command execution logs",
		Group = "Log Files",
		Order = 2)]
	string CommandLog,

	[property: SharpConfig(
		Name = "wizard_log",
		Category = "Log",
		Description = "File path for wizard/admin activity logs",
		Group = "Log Files",
		Order = 3)]
	string WizardLog,

	[property: SharpConfig(
		Name = "chkpt_log",
		Category = "Log",
		Description = "File path for database checkpoint logs",
		Group = "Log Files",
		Order = 4)]
	string CheckpointLog,

	[property: SharpConfig(
		Name = "trace_log",
		Category = "Log",
		Description = "File path for detailed trace information",
		Group = "Log Files",
		Order = 5)]
	string TraceLog,

	[property: SharpConfig(
		Name = "connect_log",
		Category = "Log",
		Description = "File path for player connection logs",
		Group = "Log Files",
		Order = 6)]
	string ConnectLog,

	[property: SharpConfig(
		Name = "mem_check",
		Category = "Log",
		Description = "Enable memory usage checking and logging",
		Group = "Diagnostics",
		Order = 1)]
	bool MemoryCheck,

	[property: SharpConfig(
		Name = "use_connlog",
		Category = "Log",
		Description = "Enable detailed connection logging",
		Group = "Log Content",
		Order = 3)]
	bool UseConnLog
);
