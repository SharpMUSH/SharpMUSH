﻿namespace SharpMUSH.Configuration.Options;

public record FileOptions(
	[property: PennConfig(Name = "input_database")] string InputDatabase = "data/indb", // Meaningless
	[property: PennConfig(Name = "output_database")] string OutputDatabase = "data/outdb", // Meaningless
	[property: PennConfig(Name = "crash_database")] string CrashDatabase = "data/PANIC.db", // Meaningless
	[property: PennConfig(Name = "mail_database")] string MailDatabase = "data/maildb", // Meaningless
	[property: PennConfig(Name = "chat_database")] string ChatDatabase = "data/chatdb", // Meaningless
	[property: PennConfig(Name = "compress_suffix")] string CompressSuffix = ".gz", // Meaningless
	[property: PennConfig(Name = "compress_program")] string CompressProgram = "gzip", // Meaningless
	[property: PennConfig(Name = "uncompress_program")] string UnCompressProgram = "gunzip", // Meaningless
	[property: PennConfig(Name = "access_file")] string AccessFile = "access.cnf",
	[property: PennConfig(Name = "names_file")] string NamesFile = "names.cnf",
	[property: PennConfig(Name = "chunk_swap_file")] string ChunkSwapFile = "data/chunkswap", // Meaningless
	[property: PennConfig(Name = "chunk_swap_initial_size")] string ChunkSwapInitialSize = "2048", // Meaningless
	[property: PennConfig(Name = "chunk_cache_memory")] string ChunkCacheMemory = "1000000", // Meaningless
	[property: PennConfig(Name = "ssl_private_key_file")] string? SSLPrivateKeyFile = null,
	[property: PennConfig(Name = "ssl_certificate_file")] string? SSLCertificateFile = null,
	[property: PennConfig(Name = "ssl_ca_file")] string? SSLCAFile = null,
	[property: PennConfig(Name = "ssl_ca_dir")] string? SSLCADirectory = null,
	[property: PennConfig(Name = "dict_file")] string? DictionaryFile = null,
	[property: PennConfig(Name = "colors_file")] string? ColorsFile = "txt/colors.json"
);