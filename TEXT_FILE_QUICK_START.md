# Text File System - Quick Implementation Reference

## Quick Start Guide

This is a condensed reference for implementing the text file system. For full details, see `TEXT_FILE_SYSTEM_PLAN.md`.

## Phase 1: Core Service (1-2 days)

### Step 1: Create Interface
**File**: `SharpMUSH.Library/Services/Interfaces/ITextFileService.cs`

```csharp
public interface ITextFileService
{
    Task<string> ListEntriesAsync(string fileName, string separator = " ");
    Task<string?> GetEntryAsync(string fileName, string entryName);
    Task<IEnumerable<string>> ListFilesAsync(string? directory = null);
    Task<string?> GetFileContentAsync(string fileName);
    Task ReindexAsync();
    Dictionary<string, string> GetHelpIndex();
}
```

### Step 2: Add Configuration
**File**: `SharpMUSH.Configuration/Options/TextFileOptions.cs`

```csharp
public record TextFileOptions(
    [property: SharpConfig(Name = "text_files_directory", Category = "File")]
    string TextFilesDirectory,
    
    [property: SharpConfig(Name = "help_files_directory", Category = "File")]
    string HelpFilesDirectory,
    
    [property: SharpConfig(Name = "enable_markdown_rendering", Category = "File")]
    bool EnableMarkdownRendering,
    
    [property: SharpConfig(Name = "text_files_cache_on_startup", Category = "File")]
    bool CacheOnStartup
);
```

**Update**: `SharpMUSH.Configuration/Options/SharpMUSHOptions.cs`
```csharp
public record SharpMUSHOptions
{
    // ... existing properties ...
    public TextFileOptions TextFile { get; init; } = new(
        TextFilesDirectory: "text_files",
        HelpFilesDirectory: "help",
        EnableMarkdownRendering: true,
        CacheOnStartup: true
    );
}
```

### Step 3: Implement Service
**File**: `SharpMUSH.Implementation/Services/TextFileService.cs`

```csharp
public class TextFileService : ITextFileService
{
    private readonly IOptions<SharpMUSHOptions> _options;
    private readonly ILogger<TextFileService> _logger;
    private readonly Dictionary<string, Dictionary<string, string>> _indexedFiles = new();
    
    public TextFileService(
        IOptions<SharpMUSHOptions> options,
        ILogger<TextFileService> logger)
    {
        _options = options;
        _logger = logger;
        
        if (_options.Value.TextFile.CacheOnStartup)
        {
            _ = IndexAllFilesAsync();
        }
    }
    
    public async Task<string> ListEntriesAsync(string fileName, string separator = " ")
    {
        var entries = GetFileIndex(fileName);
        return string.Join(separator, entries.Keys);
    }
    
    public async Task<string?> GetEntryAsync(string fileName, string entryName)
    {
        var entries = GetFileIndex(fileName);
        return entries.TryGetValue(entryName.ToUpper(), out var content) 
            ? content 
            : null;
    }
    
    public async Task<IEnumerable<string>> ListFilesAsync(string? directory = null)
    {
        var basePath = GetBasePath(directory);
        var files = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories);
        return files.Select(Path.GetFileName);
    }
    
    public async Task<string?> GetFileContentAsync(string fileName)
    {
        var fullPath = GetFullPath(fileName);
        if (!File.Exists(fullPath)) return null;
        return await File.ReadAllTextAsync(fullPath);
    }
    
    public async Task ReindexAsync()
    {
        _indexedFiles.Clear();
        await IndexAllFilesAsync();
    }
    
    public Dictionary<string, string> GetHelpIndex()
    {
        var helpDir = _options.Value.TextFile.HelpFilesDirectory;
        return _indexedFiles.TryGetValue(helpDir, out var index) 
            ? index 
            : new Dictionary<string, string>();
    }
    
    private Dictionary<string, string> GetFileIndex(string fileName)
    {
        var fullPath = GetFullPath(fileName);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {fileName}");
        
        if (!_indexedFiles.TryGetValue(fileName, out var index))
        {
            index = IndexFile(fullPath);
            _indexedFiles[fileName] = index;
        }
        
        return index;
    }
    
    private Dictionary<string, string> IndexFile(string fullPath)
    {
        // For .txt files, use PennMUSH format
        if (fullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var fileInfo = new FileInfo(fullPath);
            var result = Helpfiles.Index(fileInfo);
            return result.IsT0 ? result.AsT0 : new Dictionary<string, string>();
        }
        
        // For .md files, treat whole file as single entry
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        var content = File.ReadAllText(fullPath);
        return new Dictionary<string, string> { { fileName.ToUpper(), content } };
    }
    
    private async Task IndexAllFilesAsync()
    {
        var textDir = _options.Value.TextFile.TextFilesDirectory;
        if (!Directory.Exists(textDir)) return;
        
        var files = Directory.GetFiles(textDir, "*.*", SearchOption.AllDirectories);
        
        foreach (var file in files)
        {
            try
            {
                var fileName = Path.GetFileName(file);
                _indexedFiles[fileName] = IndexFile(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index file: {File}", file);
            }
        }
    }
    
    private string GetBasePath(string? directory)
    {
        var baseDir = _options.Value.TextFile.TextFilesDirectory;
        return string.IsNullOrEmpty(directory)
            ? baseDir
            : Path.Combine(baseDir, directory);
    }
    
    private string GetFullPath(string fileName)
    {
        var baseDir = _options.Value.TextFile.TextFilesDirectory;
        return Path.Combine(baseDir, fileName);
    }
}
```

### Step 4: Register Service
**File**: `SharpMUSH.Server/Program.cs` (or wherever DI is configured)

```csharp
builder.Services.AddSingleton<ITextFileService, TextFileService>();
```

## Phase 2: Functions (1 day)

### textentries()
**File**: `SharpMUSH.Implementation/Functions/TextFunctions.cs`

```csharp
public partial class Functions
{
    private static ITextFileService? TextFileService;
    
    public Functions(ITextFileService textFileService)
    {
        TextFileService = textFileService;
    }
    
    [SharpFunction(Name = "textentries", MinArgs = 1, MaxArgs = 2)]
    public static async ValueTask<CallState> TextEntries(
        IMUSHCodeParser parser, SharpFunctionAttribute _2)
    {
        var args = parser.CurrentState.Arguments;
        var fileName = args["0"].Message!.ToPlainText();
        var separator = args.TryGetValue("1", out var sep) 
            ? sep.Message!.ToPlainText() 
            : " ";
        
        try
        {
            var entries = await TextFileService!.ListEntriesAsync(fileName, separator);
            return new CallState(entries);
        }
        catch (FileNotFoundException)
        {
            return new CallState("#-1 FILE NOT FOUND");
        }
    }
    
    [SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2)]
    public static async ValueTask<CallState> TextFile(
        IMUSHCodeParser parser, SharpFunctionAttribute _2)
    {
        var args = parser.CurrentState.Arguments;
        var fileName = args["0"].Message!.ToPlainText();
        var entryName = args["1"].Message!.ToPlainText();
        
        try
        {
            var content = await TextFileService!.GetEntryAsync(fileName, entryName);
            return content != null 
                ? new CallState(content)
                : new CallState("#-1 ENTRY NOT FOUND");
        }
        catch (FileNotFoundException)
        {
            return new CallState("#-1 FILE NOT FOUND");
        }
    }
}
```

### help command
**File**: `SharpMUSH.Implementation/Commands/GeneralCommands.cs`

```csharp
[SharpCommand(Name = "HELP", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
public static async ValueTask<Option<CallState>> Help(
    IMUSHCodeParser parser, SharpCommandAttribute _2)
{
    var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
    var args = parser.CurrentState.Arguments;
    
    if (args.Count == 0)
    {
        await NotifyService!.Notify(executor, "Use 'help <topic>' for help on a specific topic.");
        return CallState.Empty;
    }
    
    var topic = args["0"].Message!.ToPlainText().ToUpper();
    var helpIndex = TextFileService!.GetHelpIndex();
    
    if (!helpIndex.TryGetValue(topic, out var helpText))
    {
        await NotifyService!.Notify(executor, $"No help available for '{topic}'.");
        return CallState.Empty;
    }
    
    await NotifyService!.Notify(executor, helpText);
    return CallState.Empty;
}
```

## Phase 3: Markdown Rendering (Optional)

### Add to ITextFileService
```csharp
Task<MString> RenderToAnsiAsync(string content);
Task<string> RenderToHtmlAsync(string content);
```

### Implement in TextFileService
```csharp
public async Task<MString> RenderToAnsiAsync(string content)
{
    if (!_options.Value.TextFile.EnableMarkdownRendering)
        return MModule.single(content);
    
    return RecursiveMarkdownHelper.RenderMarkdown(content, 78);
}

public async Task<string> RenderToHtmlAsync(string content)
{
    var pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();
    
    return Markdown.ToHtml(content, pipeline);
}
```

## Directory Structure

Create these directories in your SharpMUSH installation:

```
text_files/
├── help/
│   ├── commands.txt
│   ├── functions.txt
│   └── getting-started.md
├── news/
│   └── announcements.txt
├── events/
│   └── calendar.md
└── backups/
    └── (automated backups go here)
```

## Example Help File (PennMUSH format)

**File**: `text_files/help/commands.txt`

```
& @EMIT
& EMIT
@emit <message>

Emits a message to everyone in the room. The message is not attributed
to anyone in particular.

Example:
  @emit The lights flicker ominously.

See also: @oemit, @pemit, @remit

& @PEMIT
& PEMIT  
@pemit <player>=<message>

Sends a private message to a specific player.

Example:
  @pemit Bob=You hear a whisper in the darkness.

See also: @emit, page
```

## Example Markdown Help File

**File**: `text_files/help/getting-started.md`

```markdown
# Getting Started with SharpMUSH

Welcome to SharpMUSH! This guide will help you get started.

## First Steps

1. **Create a character**: Use the `create` command
2. **Set your description**: `@describe me=<description>`
3. **Look around**: Use `look` to see your surroundings

## Basic Commands

- `look` - See your current location
- `say <message>` - Talk to people in the room
- `pose <action>` - Perform an action
- `who` - See who's online

## Getting Help

Use `help <topic>` to get help on any topic.

For a list of all commands, type `help commands`.
```

## Testing

### Test the Service
```csharp
[Test]
public async Task TestTextFileService()
{
    var service = serviceProvider.GetRequiredService<ITextFileService>();
    
    // Test listing entries
    var entries = await service.ListEntriesAsync("commands.txt");
    await Assert.That(entries).Contains("@EMIT");
    
    // Test getting entry
    var content = await service.GetEntryAsync("commands.txt", "@EMIT");
    await Assert.That(content).IsNotNull();
}
```

### Test Functions
```csharp
[Test]
[Arguments("textentries(commands.txt)", "@EMIT @PEMIT")]
public async Task TestTextEntries(string input, string expected)
{
    var result = await Parser.FunctionParse(MModule.single(input));
    await Assert.That(result?.Message?.ToString()).Contains(expected);
}

[Test]
[Arguments("textfile(commands.txt,@EMIT)", "Emits a message")]
public async Task TestTextFile(string input, string expected)
{
    var result = await Parser.FunctionParse(MModule.single(input));
    await Assert.That(result?.Message?.ToString()).Contains(expected);
}
```

### Test Help Command
In-game testing:
```
> help @emit
@EMIT / EMIT
@emit <message>

Emits a message to everyone in the room...
```

## Common Issues

### Files not found
- Check `text_files_directory` path in config
- Ensure directories exist
- Check file permissions

### No entries returned
- Verify file format (& INDEX for .txt files)
- Check file encoding (UTF-8)
- Look for parsing errors in logs

### Help not working
- Verify files are indexed (check `ReindexAsync()`)
- Check topic name (case-insensitive)
- Ensure help files are in correct directory

## Configuration Checklist

- [ ] Set `text_files_directory` in config
- [ ] Create directory structure
- [ ] Add help files (copy from PennMUSH or create new)
- [ ] Set `text_files_cache_on_startup = yes`
- [ ] Set `enable_markdown_rendering` as desired
- [ ] Test with `help` command
- [ ] Test `textentries()` and `textfile()` functions

## Next Steps

After basic implementation:

1. Add web API endpoints (see full plan)
2. Create Blazor file editor
3. Add backup functionality
4. Implement search
5. Add file watching for development

## Resources

- Full Plan: `TEXT_FILE_SYSTEM_PLAN.md`
- Architecture Options: `TEXT_FILE_ARCHITECTURE_OPTIONS.md`
- PennMUSH Helpfiles: `SharpMUSH.Documentation/Helpfiles/PennMUSH/`
- Existing Helpfiles class: `SharpMUSH.Documentation/Helpfiles.cs`
- Markdown Renderer: `SharpMUSH.Documentation/MarkdownToAsciiRenderer/`

## Estimated Time

- **Phase 1** (Core Service): 4-6 hours
- **Phase 2** (Functions/Commands): 2-3 hours  
- **Phase 3** (Markdown): 1-2 hours
- **Testing**: 2 hours

**Total MVP**: 1-2 days for a working implementation
