using SharpMUSH.Configuration;

namespace SharpMUSH.Configuration.Options;

public record TextFileOptions(
	[property: SharpConfig(
		Name = "text_files_directory",
		Category = "TextFile",
		Description = "Base directory for text files. All subdirectories are auto-discovered as categories.",
		Group = "File Management",
		Order = 1)]
	string TextFilesDirectory,

	[property: SharpConfig(
		Name = "enable_markdown_rendering",
		Category = "TextFile",
		Description = "Enable automatic markdown to ANSI rendering",
		Group = "Rendering",
		Order = 1)]
	bool EnableMarkdownRendering,

	[property: SharpConfig(
		Name = "text_files_cache_on_startup",
		Category = "TextFile",
		Description = "Cache and index all text files on startup",
		Group = "Performance",
		Order = 1)]
	bool CacheOnStartup
);
