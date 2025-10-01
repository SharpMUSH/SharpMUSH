namespace SharpMUSH.Configuration.Options;

public record FileOptions(
	[property: PennConfig(Name = "input_database", Description = "Input database file for loading data")] string InputDatabase = "data/indb", // Meaningless
	[property: PennConfig(Name = "output_database", Description = "Output database file for saving data")] string OutputDatabase = "data/outdb", // Meaningless
	[property: PennConfig(Name = "crash_database", Description = "Emergency database file created on crashes")] string CrashDatabase = "data/PANIC.db", // Meaningless
	[property: PennConfig(Name = "mail_database", Description = "Database file for mail system data")] string MailDatabase = "data/maildb", // Meaningless
	[property: PennConfig(Name = "chat_database", Description = "Database file for chat system data")] string ChatDatabase = "data/chatdb", // Meaningless
	[property: PennConfig(Name = "compress_suffix", Description = "File extension for compressed database files")] string CompressSuffix = ".gz", // Meaningless
	[property: PennConfig(Name = "compress_program", Description = "Program used to compress database files")] string CompressProgram = "gzip", // Meaningless
	[property: PennConfig(Name = "uncompress_program", Description = "Program used to decompress database files")] string UnCompressProgram = "gunzip", // Meaningless
	[property: PennConfig(Name = "access_file", Description = "File containing access control rules")] string AccessFile = "access.cnf",
	[property: PennConfig(Name = "names_file", Description = "File containing restricted player names")] string NamesFile = "names.cnf",
	[property: PennConfig(Name = "chunk_swap_file", Description = "File for chunk-based memory swapping")] string ChunkSwapFile = "data/chunkswap", // Meaningless
	[property: PennConfig(Name = "chunk_swap_initial_size", Description = "Initial size of chunk swap file in bytes")] string ChunkSwapInitialSize = "2048", // Meaningless
	[property: PennConfig(Name = "chunk_cache_memory", Description = "Amount of memory allocated for chunk caching")] string ChunkCacheMemory = "1000000", // Meaningless
	[property: PennConfig(Name = "ssl_private_key_file", Description = "SSL private key file for secure connections")] string? SSLPrivateKeyFile = null,
	[property: PennConfig(Name = "ssl_certificate_file", Description = "SSL certificate file for secure connections")] string? SSLCertificateFile = null,
	[property: PennConfig(Name = "ssl_ca_file", Description = "SSL certificate authority file")] string? SSLCAFile = null,
	[property: PennConfig(Name = "ssl_ca_dir", Description = "Directory containing SSL certificate authorities")] string? SSLCADirectory = null,
	[property: PennConfig(Name = "dict_file", Description = "Dictionary file for spell checking and word lists")] string? DictionaryFile = null,
	[property: PennConfig(Name = "colors_file", Description = "JSON file defining color codes and mappings")] string? ColorsFile = "txt/colors.json"
);