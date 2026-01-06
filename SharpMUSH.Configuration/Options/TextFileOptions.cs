using SharpMUSH.Configuration;

namespace SharpMUSH.Configuration.Options;

public record TextFileOptions(
	[property: SharpConfig(
		Name = "text_files_directory",
		Description = "Base directory for text files. All subdirectories are auto-discovered as categories.",
		Category = "File")]
	string TextFilesDirectory,

	[property: SharpConfig(
		Name = "enable_markdown_rendering",
		Description = "Enable automatic markdown to ANSI rendering",
		Category = "File")]
	bool EnableMarkdownRendering,

	[property: SharpConfig(
		Name = "text_files_cache_on_startup",
		Description = "Cache and index all text files on startup",
		Category = "File")]
	bool CacheOnStartup
);
