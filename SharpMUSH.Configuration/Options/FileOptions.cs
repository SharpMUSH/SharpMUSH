namespace SharpMUSH.Configuration.Options;

public record FileOptions(
	[property: PennConfig(Name = "input_database", Description = "Input database file for loading data", DefaultValue = "ignored")] string InputDatabase,
	[property: PennConfig(Name = "output_database", Description = "Output database file for saving data", DefaultValue = "ignored")] string OutputDatabase,
	[property: PennConfig(Name = "crash_database", Description = "Emergency database file created on crashes", DefaultValue = "ignored")] string CrashDatabase,
	[property: PennConfig(Name = "mail_database", Description = "Database file for mail system data", DefaultValue = "ignored")] string MailDatabase,
	[property: PennConfig(Name = "chat_database", Description = "Database file for chat system data", DefaultValue = "ignored")] string ChatDatabase,
	[property: PennConfig(Name = "compress_suffix", Description = "File extension for compressed database files", DefaultValue = "ignored")] string CompressSuffix,
	[property: PennConfig(Name = "compress_program", Description = "Program used to compress database files", DefaultValue = "ignored")] string CompressProgram,
	[property: PennConfig(Name = "uncompress_program", Description = "Program used to decompress database files", DefaultValue = "ignored")] string UnCompressProgram,
	[property: PennConfig(Name = "access_file", Description = "File containing access control rules", DefaultValue = "access.cnf")] string AccessFile,
	[property: PennConfig(Name = "names_file", Description = "File containing restricted player names", DefaultValue = "names.cnf")] string NamesFile,
	[property: PennConfig(Name = "chunk_swap_file", Description = "File for chunk-based memory swapping", DefaultValue = "ignored")] string ChunkSwapFile,
	[property: PennConfig(Name = "chunk_swap_initial_size", Description = "Initial size of chunk swap file in bytes", DefaultValue = "ignored")] string ChunkSwapInitialSize,
	[property: PennConfig(Name = "chunk_cache_memory", Description = "Amount of memory allocated for chunk caching", DefaultValue = "ignored")] string ChunkCacheMemory,
	[property: PennConfig(Name = "ssl_private_key_file", Description = "SSL private key file for secure connections", DefaultValue = null)] string? SSLPrivateKeyFile,
	[property: PennConfig(Name = "ssl_certificate_file", Description = "SSL certificate file for secure connections", DefaultValue = null)] string? SSLCertificateFile,
	[property: PennConfig(Name = "ssl_ca_file", Description = "SSL certificate authority file", DefaultValue = null)] string? SSLCAFile,
	[property: PennConfig(Name = "ssl_ca_dir", Description = "Directory containing SSL certificate authorities", DefaultValue = null)] string? SSLCADirectory,
	[property: PennConfig(Name = "dict_file", Description = "Dictionary file for spell checking and word lists", DefaultValue = null)] string? DictionaryFile,
	[property: PennConfig(Name = "colors_file", Description = "JSON file defining color codes and mappings", DefaultValue = "colors.json")] string? ColorsFile
);