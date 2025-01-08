namespace SharpMUSH.Configuration.Options;

public record FileOptions(
	string InputDatabase = "data/indb", // Meaningless
	string OutputDatabase = "data/outdb", // Meaningless
	string CrashDatabase = "data/PANIC.db", // Meaningless
	string MailDatabase = "data/maildb", // Meaningless
	string ChatDatabase = "data/chatdb", // Meaningless
	string CompressSuffix = ".gz", // Meaningless
	string CompressProgram = "gzip", // Meaningless
	string UnCompressProgram = "gunzip", // Meaningless
	string AccessFile = "access.cnf",
	string NamesFile = "names.cnf",
	string ChunkSwapFile = "data/chunkswap", // Meaningless
	string ChunkSwapInitialSize = "2048", // Meaningless
	string ChunkCacheMemory = "1000000", // Meaningless
	string? SSLPrivateKeyFile = null,
	string? SSLCertificateFile = null,
	string? SSLCAFile = null,
	string? SSLCADirectory = null,
	string? DictionaryFile = null,
	string? ColorsFile = "txt/colors.json"
);