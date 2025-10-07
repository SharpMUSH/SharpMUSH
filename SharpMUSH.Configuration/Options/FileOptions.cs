﻿namespace SharpMUSH.Configuration.Options;

public record FileOptions(
	/*
	 Irrelevant to SharpMUSH
	[property: PennConfig(
Name  = "input_database",
Description  = "Input database file for loading data")] string InputDatabase,
	[property: PennConfig(
Name  = "output_database",
Description  = "Output database file for saving data")] string OutputDatabase,
	[property: PennConfig(
Name  = "crash_database",
Description  = "Emergency database file created on crashes")] string CrashDatabase,
	[property: PennConfig(
Name  = "mail_database",
Description  = "Database file for mail system data")] string MailDatabase,
	[property: PennConfig(
Name  = "chat_database",
Description  = "Database file for chat system data")] string ChatDatabase,
	[property: PennConfig(
Name  = "compress_suffix",
Description  = "File extension for compressed database files")] string CompressSuffix,
	[property: PennConfig(
Name  = "compress_program",
Description  = "Program used to compress database files")] string CompressProgram,
	[property: PennConfig(
Name  = "uncompress_program",
Description  = "Program used to decompress database files")] string UnCompressProgram,
*/
	[property: SharpConfig(
		Name = "access_file",
		Description = "File containing access control rules", Category = "File")]
	string AccessFile,
	[property: SharpConfig(
		Name = "names_file",
		Description = "File containing restricted player names", Category = "File")]
	string NamesFile,
	/*
[property: PennConfig(
Name  = "chunk_swap_file",
Description  = "File for chunk-based memory swapping")] string ChunkSwapFile,
[property: PennConfig(
Name  = "chunk_swap_initial_size",
Description  = "Initial size of chunk swap file in bytes")] string ChunkSwapInitialSize,
[property: PennConfig(
Name  = "chunk_cache_memory",
Description  = "Amount of memory allocated for chunk caching")] string ChunkCacheMemory,
*/
	[property: SharpConfig(
		Name = "ssl_private_key_file",
		Description = "SSL private key file for secure connections", Category = "File")]
	string? SSLPrivateKeyFile,
	[property: SharpConfig(
		Name = "ssl_certificate_file",
		Description = "SSL certificate file for secure connections", Category = "File")]
	string? SSLCertificateFile,
	[property: SharpConfig(
		Name = "ssl_ca_file",
		Description = "SSL certificate authority file", Category = "File")]
	string? SSLCAFile,
	[property: SharpConfig(
		Name = "ssl_ca_dir",
		Description = "Directory containing SSL certificate authorities", Category = "File")]
	string? SSLCADirectory,
	[property: SharpConfig(
		Name = "dict_file",
		Description = "Dictionary file for spell checking and word lists", Category = "File")]
	string? DictionaryFile,
	[property: SharpConfig(
		Name = "colors_file",
		Description = "JSON file defining color codes and mappings", Category = "File")]
	string? ColorsFile
);