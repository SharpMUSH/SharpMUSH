namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	[property: SharpConfig(
		Name = "use_syslog",
		Category = "Log",
		Description = "Send log messages to system syslog instead of files",
		Group = "Log Destination",
		Order = 1,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	bool UseSyslog,

	[property: SharpConfig(
		Name = "log_commands",
		Category = "Log",
		Description = "Log all commands executed by players",
		Group = "Log Content",
		Order = 1,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	bool LogCommands,

	[property: SharpConfig(
		Name = "log_forces",
		Category = "Log",
		Description = "Log all @force commands and their results",
		Group = "Log Content",
		Order = 2,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	bool LogForces,

	[property: SharpConfig(
		Name = "error_log",
		Category = "Log",
		Description = "File path for error and warning messages",
		Group = "Log Files",
		Order = 1,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	string ErrorLog,

	[property: SharpConfig(
		Name = "command_log",
		Category = "Log",
		Description = "File path for command execution logs",
		Group = "Log Files",
		Order = 2,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	string CommandLog,

	[property: SharpConfig(
		Name = "wizard_log",
		Category = "Log",
		Description = "File path for wizard/admin activity logs",
		Group = "Log Files",
		Order = 3,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section. Staff actions are in the audit log.")]
	string WizardLog,

	[property: SharpConfig(
		Name = "checkpt_log",
		Category = "Log",
		Description = "File path for database checkpoint logs",
		Group = "Log Files",
		Order = 4,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	string CheckpointLog,

	[property: SharpConfig(
		Name = "trace_log",
		Category = "Log",
		Description = "File path for detailed trace information",
		Group = "Log Files",
		Order = 5,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	string TraceLog,

	[property: SharpConfig(
		Name = "connect_log",
		Category = "Log",
		Description = "File path for player connection logs",
		Group = "Log Files",
		Order = 6,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	string ConnectLog,

	[property: SharpConfig(
		Name = "mem_check",
		Category = "Log",
		Description = "Enable memory usage checking and logging",
		Group = "Diagnostics",
		Order = 1,
		Unused = "Logging goes through Serilog, set in appsettings.json's Serilog section.")]
	bool MemoryCheck,

	[property: SharpConfig(
		Name = "use_connlog",
		Category = "Log",
		Description = "Enable detailed connection logging",
		Group = "Log Content",
		Order = 3)]
	bool UseConnLog
);
