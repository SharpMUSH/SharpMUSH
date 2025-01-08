namespace SharpMUSH.Configuration.Options;

public record LogOptions(
	bool UseSyslog = false,
	bool LogCommands = false,
	bool LogForces = true,
	string ErrorLog = "log/netmush.log",
	string CommandLog = "log/command.log",
	string WizardLog = "log/wizard.log",
	string CheckpointLog = "log/checkpoint.log",
	string TraceLog = "log/trace.log",
	string ConnectLog = "log/connect.log",
	bool MemoryCheck = false,
	bool UseConnLog = true
);