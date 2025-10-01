namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	[property: PennConfig(Name = "use_syslog", Description = "Send log messages to system syslog instead of files", DefaultValue = "true")] bool UseSyslog,
	[property: PennConfig(Name = "log_commands", Description = "Log all commands executed by players", DefaultValue = "false")] bool LogCommands,
	[property: PennConfig(Name = "log_forces", Description = "Log all @force commands and their results", DefaultValue = "false")] bool LogForces,
	[property: PennConfig(Name = "error_log", Description = "File path for error and warning messages", DefaultValue = "logs/err.log")] string ErrorLog,
	[property: PennConfig(Name = "command_log", Description = "File path for command execution logs", DefaultValue = "logs/cmd.log")] string CommandLog,
	[property: PennConfig(Name = "wizard_log", Description = "File path for wizard/admin activity logs", DefaultValue = "logs/wiz.log")] string WizardLog,
	[property: PennConfig(Name = "chkpt_log", Description = "File path for database checkpoint logs", DefaultValue = "logs/ckpt.log")] string CheckpointLog,
	[property: PennConfig(Name = "trace_log", Description = "File path for detailed trace information", DefaultValue = "logs/trace.log")] string TraceLog,
	[property: PennConfig(Name = "connect_log", Description = "File path for player connection logs", DefaultValue = "logs/connect.log")] string ConnectLog,
	[property: PennConfig(Name = "mem_check", Description = "Enable memory usage checking and logging", DefaultValue = "false")] bool MemoryCheck,
	[property: PennConfig(Name = "use_connlog", Description = "Enable detailed connection logging", DefaultValue = "false")] bool UseConnLog
);