namespace SharpMUSH.Configuration.Options;

public record FileOptions(
	[PennConfig(Name = "input_database")] string InputDatabase = "data/indb", // Meaningless
	[PennConfig(Name = "output_database")] string OutputDatabase = "data/outdb", // Meaningless
	[PennConfig(Name = "crash_database")] string CrashDatabase = "data/PANIC.db", // Meaningless
	[PennConfig(Name = "mail_database")] string MailDatabase = "data/maildb", // Meaningless
	[PennConfig(Name = "chat_database")] string ChatDatabase = "data/chatdb", // Meaningless
	[PennConfig(Name = "compress_suffix")] string CompressSuffix = ".gz", // Meaningless
	[PennConfig(Name = "compress_program")] string CompressProgram = "gzip", // Meaningless
	[PennConfig(Name = "uncompress_program")] string UnCompressProgram = "gunzip", // Meaningless
	[PennConfig(Name = "access_file")] string AccessFile = "access.cnf",
	[PennConfig(Name = "namess_file")] string NamesFile = "names.cnf",
	[PennConfig(Name = "chunkswap_file")] string ChunkSwapFile = "data/chunkswap", // Meaningless
	[PennConfig(Name = "chunkswap_initialsize")] string ChunkSwapInitialSize = "2048", // Meaningless
	[PennConfig(Name = "chunk_cache_memory")] string ChunkCacheMemory = "1000000", // Meaningless
	[PennConfig(Name = "ssl_private_key")] string? SSLPrivateKeyFile = null,
	[PennConfig(Name = "ssl_certificate")] string? SSLCertificateFile = null,
	[PennConfig(Name = "ssl_ca")] string? SSLCAFile = null,
	[PennConfig(Name = "ssl_ca_directory")] string? SSLCADirectory = null,
	[PennConfig(Name = "dictionary")] string? DictionaryFile = null,
	[PennConfig(Name = "colors")] string? ColorsFile = "txt/colors.json"
);