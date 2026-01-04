# Text File System Implementation Plan

## Executive Summary

This document outlines a comprehensive plan for implementing text file reading capabilities in SharpMUSH, including the `textentries()` and `textfile()` functions, a `help` command, markdown-to-ANSI/HTML conversion, and web-based file editing through the client interface.

## Current State

### Existing Infrastructure
1. **SharpMUSH.Documentation.Helpfiles**: Indexes `.txt` files with PennMUSH-style `& INDEX` entries
2. **MarkdownToAsciiRenderer**: Converts CommonMark/Markdown to ANSI-formatted text
3. **Web Client Wiki Components**: Blazor-based markdown editing (WikiEdit, WikiView, WikiDisplay)
4. **rendermarkdown() Function**: Already implemented for markdown-to-ANSI conversion
5. **Configuration System**: SharpConfig attributes for options

### Missing Components
1. No general-purpose text file service
2. `textentries()` and `textfile()` functions are stubbed (return NotSupported)
3. No `help` command implementation
4. No web API for text file management
5. No web UI for browsing/editing text files
6. No text file configuration options

## Architecture Overview

### Component Hierarchy
```
SharpMUSH.Library (Interfaces)
├── ITextFileService
└── Services/Interfaces/

SharpMUSH.Implementation (Implementation)
├── Services/TextFileService
├── Functions/TextFunctions (textentries, textfile, textsearch)
└── Commands/HelpCommand

SharpMUSH.Configuration (Configuration)
└── Options/TextFileOptions

SharpMUSH.Server (API)
└── Controllers/TextFileController

SharpMUSH.Client (Web UI)
├── Components/TextFileBrowser
├── Components/TextFileEditor
└── Pages/Admin/TextFileManagement
```

## Detailed Design

### 1. Service Interface (ITextFileService)

**Location**: `SharpMUSH.Library/Services/Interfaces/ITextFileService.cs`

```csharp
namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for managing text files (help, news, events, etc.)
/// Supports both PennMUSH .txt format and markdown .md files
/// </summary>
public interface ITextFileService
{
	/// <summary>
	/// Lists all entry names/indexes in a text file
	/// </summary>
	/// <param name="fileName">Name of the text file (without path)</param>
	/// <param name="separator">Separator for the returned list (default: space)</param>
	/// <returns>Space or separator-delimited list of entry names</returns>
	Task<string> ListEntriesAsync(string fileName, string separator = " ");
	
	/// <summary>
	/// Gets the content of a specific entry from a text file
	/// </summary>
	/// <param name="fileName">Name of the text file</param>
	/// <param name="entryName">Name of the entry (case-insensitive)</param>
	/// <returns>Entry content, or null if not found</returns>
	Task<string?> GetEntryAsync(string fileName, string entryName);
	
	/// <summary>
	/// Lists all text files in a directory
	/// </summary>
	/// <param name="directory">Directory name (help, news, etc.) or null for all</param>
	/// <returns>List of file names</returns>
	Task<IEnumerable<string>> ListFilesAsync(string? directory = null);
	
	/// <summary>
	/// Gets the full content of a text file
	/// </summary>
	/// <param name="fileName">Name of the text file</param>
	/// <returns>File content, or null if not found</returns>
	Task<string?> GetFileContentAsync(string fileName);
	
	/// <summary>
	/// Searches for entries matching a pattern in a text file
	/// </summary>
	/// <param name="fileName">Name of the text file</param>
	/// <param name="pattern">Search pattern (supports wildcards)</param>
	/// <returns>List of matching entry names</returns>
	Task<IEnumerable<string>> SearchEntriesAsync(string fileName, string pattern);
	
	/// <summary>
	/// Saves content to a text file (creates backup first)
	/// </summary>
	/// <param name="fileName">Name of the text file</param>
	/// <param name="content">New file content</param>
	/// <param name="editor">DBRef of the editing player</param>
	/// <returns>True if successful</returns>
	Task<bool> SaveFileAsync(string fileName, string content, DBRef editor);
	
	/// <summary>
	/// Deletes a text file (creates backup first)
	/// </summary>
	/// <param name="fileName">Name of the text file</param>
	/// <param name="editor">DBRef of the deleting player</param>
	/// <returns>True if successful</returns>
	Task<bool> DeleteFileAsync(string fileName, DBRef editor);
	
	/// <summary>
	/// Creates a timestamped backup of a text file
	/// </summary>
	/// <param name="fileName">Name of the text file</param>
	/// <returns>Backup file path</returns>
	Task<string> CreateBackupAsync(string fileName);
	
	/// <summary>
	/// Re-indexes all text files (rebuilds cache)
	/// </summary>
	Task ReindexAsync();
	
	/// <summary>
	/// Gets the indexed help entries for fast lookup
	/// </summary>
	/// <returns>Dictionary mapping entry names to content</returns>
	Dictionary<string, string> GetHelpIndex();
	
	/// <summary>
	/// Renders markdown content to ANSI-formatted MarkupString
	/// </summary>
	/// <param name="content">Markdown content</param>
	/// <returns>ANSI-formatted MarkupString</returns>
	Task<MString> RenderToAnsiAsync(string content);
	
	/// <summary>
	/// Renders markdown content to HTML
	/// </summary>
	/// <param name="content">Markdown content</param>
	/// <returns>HTML string</returns>
	Task<string> RenderToHtmlAsync(string content);
}
```

### 2. Configuration Options

**Location**: `SharpMUSH.Configuration/Options/TextFileOptions.cs`

```csharp
namespace SharpMUSH.Configuration.Options;

public record TextFileOptions(
	[property: SharpConfig(
		Name = "text_files_directory",
		Description = "Base directory for text files (relative to server root)",
		Category = "File")]
	string TextFilesDirectory,
	
	[property: SharpConfig(
		Name = "help_files_directory",
		Description = "Directory for help files (relative to text_files_directory)",
		Category = "File")]
	string HelpFilesDirectory,
	
	[property: SharpConfig(
		Name = "news_files_directory",
		Description = "Directory for news files (relative to text_files_directory)",
		Category = "File")]
	string NewsFilesDirectory,
	
	[property: SharpConfig(
		Name = "events_files_directory",
		Description = "Directory for events files (relative to text_files_directory)",
		Category = "File")]
	string EventsFilesDirectory,
	
	[property: SharpConfig(
		Name = "enable_markdown_rendering",
		Description = "Enable automatic markdown to ANSI/HTML rendering",
		Category = "File")]
	bool EnableMarkdownRendering,
	
	[property: SharpConfig(
		Name = "text_files_cache_on_startup",
		Description = "Index and cache text files when server starts",
		Category = "File")]
	bool CacheOnStartup,
	
	[property: SharpConfig(
		Name = "text_files_watch_for_changes",
		Description = "Watch text files and auto-reload when changed (recommended for dev only)",
		Category = "File")]
	bool WatchForChanges,
	
	[property: SharpConfig(
		Name = "text_files_backup_enabled",
		Description = "Create backups before editing text files",
		Category = "File")]
	bool BackupEnabled,
	
	[property: SharpConfig(
		Name = "text_files_backup_directory",
		Description = "Directory for text file backups (relative to text_files_directory)",
		Category = "File")]
	string BackupDirectory
);
```

**Default Values** (in `mushcnf.dst`):
```
text_files_directory text_files
help_files_directory help
news_files_directory news
events_files_directory events
enable_markdown_rendering yes
text_files_cache_on_startup yes
text_files_watch_for_changes no
text_files_backup_enabled yes
text_files_backup_directory backups
```

### 3. Service Implementation

**Location**: `SharpMUSH.Implementation/Services/TextFileService.cs`

Key implementation details:

1. **File Indexing**
   - Leverage existing `Helpfiles` class for PennMUSH `.txt` format
   - Support both `.txt` and `.md` file formats
   - Cache indexed entries in memory for fast lookup
   - Support multiple directories (help, news, events)

2. **File Format Detection**
   ```csharp
   private bool IsMarkdownFile(string fileName) => 
       fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
   
   private bool IsPennMUSHTextFile(string fileName) => 
       fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
   ```

3. **Entry Parsing**
   - For `.txt` files: Use `Helpfiles.Index()` to extract `& INDEX` entries
   - For `.md` files: Treat entire file as single entry with filename as index

4. **Rendering**
   - For ANSI: Use existing `MarkdownToAsciiRenderer`
   - For HTML: Use Markdig with HTML renderer
   - Cache rendered output for performance

5. **File Watching** (optional)
   ```csharp
   private FileSystemWatcher? _fileWatcher;
   
   private void SetupFileWatcher()
   {
       if (!_options.WatchForChanges) return;
       
       _fileWatcher = new FileSystemWatcher(_options.TextFilesDirectory)
       {
           NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
           Filter = "*.*",
           IncludeSubdirectories = true
       };
       
       _fileWatcher.Changed += OnFileChanged;
       _fileWatcher.Created += OnFileChanged;
       _fileWatcher.Deleted += OnFileChanged;
       _fileWatcher.EnableRaisingEvents = true;
   }
   
   private async void OnFileChanged(object sender, FileSystemEventArgs e)
   {
       await ReindexAsync();
   }
   ```

6. **Backup Strategy**
   ```csharp
   private async Task<string> CreateBackupAsync(string fileName)
   {
       if (!_options.BackupEnabled) return string.Empty;
       
       var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
       var backupPath = Path.Combine(
           _options.TextFilesDirectory,
           _options.BackupDirectory,
           $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}"
       );
       
       var sourcePath = GetFullPath(fileName);
       Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
       await File.Copy(sourcePath, backupPath);
       
       return backupPath;
   }
   ```

### 4. Function Implementations

**Location**: `SharpMUSH.Implementation/Functions/TextFunctions.cs`

#### textentries()

```csharp
[SharpFunction(Name = "textentries", MinArgs = 1, MaxArgs = 2, 
    Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
public static async ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		return new CallState($"#-1 FILE NOT FOUND: {fileName}");
	}
	catch (Exception ex)
	{
		return new CallState($"#-1 ERROR: {ex.Message}");
	}
}
```

#### textfile()

```csharp
[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2, 
    Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
public static async ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
{
	var args = parser.CurrentState.Arguments;
	var fileName = args["0"].Message!.ToPlainText();
	var entryName = args["1"].Message!.ToPlainText();
	
	try
	{
		var content = await TextFileService!.GetEntryAsync(fileName, entryName);
		
		if (content == null)
		{
			return new CallState($"#-1 ENTRY NOT FOUND: {entryName}");
		}
		
		return new CallState(content);
	}
	catch (FileNotFoundException)
	{
		return new CallState($"#-1 FILE NOT FOUND: {fileName}");
	}
	catch (Exception ex)
	{
		return new CallState($"#-1 ERROR: {ex.Message}");
	}
}
```

#### textsearch()

```csharp
[SharpFunction(Name = "textsearch", MinArgs = 2, MaxArgs = 3,
    Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
public static async ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
{
	var args = parser.CurrentState.Arguments;
	var fileName = args["0"].Message!.ToPlainText();
	var pattern = args["1"].Message!.ToPlainText();
	var separator = args.TryGetValue("2", out var sep)
		? sep.Message!.ToPlainText()
		: " ";
	
	try
	{
		var entries = await TextFileService!.SearchEntriesAsync(fileName, pattern);
		return new CallState(string.Join(separator, entries));
	}
	catch (FileNotFoundException)
	{
		return new CallState($"#-1 FILE NOT FOUND: {fileName}");
	}
	catch (Exception ex)
	{
		return new CallState($"#-1 ERROR: {ex.Message}");
	}
}
```

### 5. Help Command

**Location**: `SharpMUSH.Implementation/Commands/GeneralCommands.cs`

```csharp
[SharpCommand(Name = "HELP", Switches = [], 
    Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
public static async ValueTask<Option<CallState>> Help(IMUSHCodeParser parser, SharpCommandAttribute _2)
{
	var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
	var args = parser.CurrentState.Arguments;
	
	// If no topic specified, show help index
	if (args.Count == 0)
	{
		var files = await TextFileService!.ListFilesAsync("help");
		await NotifyService!.Notify(executor, "Available help files:");
		await NotifyService!.Notify(executor, string.Join(", ", files));
		await NotifyService!.Notify(executor, "Use 'help <topic>' for specific help.");
		return CallState.Empty;
	}
	
	var topic = args["0"].Message!.ToPlainText().ToUpper();
	
	// Try to find the topic in the help index
	var helpIndex = TextFileService!.GetHelpIndex();
	
	if (!helpIndex.TryGetValue(topic, out var helpText))
	{
		await NotifyService!.Notify(executor, $"No help available for '{topic}'.");
		await NotifyService!.Notify(executor, "Try 'help' for a list of topics.");
		return CallState.Empty;
	}
	
	// Render markdown to ANSI if enabled
	var rendered = helpText;
	if (Configuration!.CurrentValue.TextFile.EnableMarkdownRendering)
	{
		var markupString = await TextFileService!.RenderToAnsiAsync(helpText);
		rendered = markupString.ToString();
	}
	
	// Send help text to player
	await NotifyService!.Notify(executor, rendered);
	
	return CallState.Empty;
}
```

### 6. Web API Endpoints

**Location**: `SharpMUSH.Server/Controllers/TextFileController.cs`

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize] // Require authentication
public class TextFileController : ControllerBase
{
	private readonly ITextFileService _textFileService;
	private readonly IPermissionService _permissionService;
	
	public TextFileController(
		ITextFileService textFileService,
		IPermissionService permissionService)
	{
		_textFileService = textFileService;
		_permissionService = permissionService;
	}
	
	[HttpGet]
	public async Task<IActionResult> ListFiles([FromQuery] string? directory = null)
	{
		var files = await _textFileService.ListFilesAsync(directory);
		return Ok(files);
	}
	
	[HttpGet("{fileName}")]
	public async Task<IActionResult> GetFile(string fileName)
	{
		var content = await _textFileService.GetFileContentAsync(fileName);
		
		if (content == null)
			return NotFound();
		
		return Ok(new { fileName, content });
	}
	
	[HttpGet("{fileName}/entries")]
	public async Task<IActionResult> GetEntries(string fileName)
	{
		var entries = await _textFileService.ListEntriesAsync(fileName, "|");
		return Ok(entries.Split('|'));
	}
	
	[HttpGet("{fileName}/entries/{entryName}")]
	public async Task<IActionResult> GetEntry(string fileName, string entryName)
	{
		var content = await _textFileService.GetEntryAsync(fileName, entryName);
		
		if (content == null)
			return NotFound();
		
		return Ok(new { fileName, entryName, content });
	}
	
	[HttpPut("{fileName}")]
	[Authorize(Roles = "Wizard")] // Require wizard permission
	public async Task<IActionResult> SaveFile(string fileName, [FromBody] TextFileUpdateRequest request)
	{
		// Get editor DBRef from authenticated user
		var editorDbRef = GetCurrentPlayerDbRef();
		
		// Validate file path to prevent directory traversal
		if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
		{
			return BadRequest("Invalid file name");
		}
		
		var success = await _textFileService.SaveFileAsync(fileName, request.Content, editorDbRef);
		
		if (success)
			return Ok();
		
		return StatusCode(500, "Failed to save file");
	}
	
	[HttpDelete("{fileName}")]
	[Authorize(Roles = "Wizard")] // Require wizard permission
	public async Task<IActionResult> DeleteFile(string fileName)
	{
		var editorDbRef = GetCurrentPlayerDbRef();
		
		if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
		{
			return BadRequest("Invalid file name");
		}
		
		var success = await _textFileService.DeleteFileAsync(fileName, editorDbRef);
		
		if (success)
			return Ok();
		
		return StatusCode(500, "Failed to delete file");
	}
	
	[HttpPost("{fileName}/backup")]
	[Authorize(Roles = "Wizard")]
	public async Task<IActionResult> CreateBackup(string fileName)
	{
		var backupPath = await _textFileService.CreateBackupAsync(fileName);
		return Ok(new { backupPath });
	}
	
	[HttpPost("reindex")]
	[Authorize(Roles = "Wizard")]
	public async Task<IActionResult> Reindex()
	{
		await _textFileService.ReindexAsync();
		return Ok();
	}
	
	[HttpPost("{fileName}/render/ansi")]
	public async Task<IActionResult> RenderToAnsi(string fileName)
	{
		var content = await _textFileService.GetFileContentAsync(fileName);
		
		if (content == null)
			return NotFound();
		
		var rendered = await _textFileService.RenderToAnsiAsync(content);
		return Ok(rendered.ToString());
	}
	
	[HttpPost("{fileName}/render/html")]
	public async Task<IActionResult> RenderToHtml(string fileName)
	{
		var content = await _textFileService.GetFileContentAsync(fileName);
		
		if (content == null)
			return NotFound();
		
		var rendered = await _textFileService.RenderToHtmlAsync(content);
		return Ok(rendered);
	}
	
	private DBRef GetCurrentPlayerDbRef()
	{
		// TODO: Implement getting DBRef from authenticated user
		// This will need to integrate with the authentication system
		return new DBRef(1);
	}
}

public record TextFileUpdateRequest(string Content);
```

### 7. Web UI Components

#### TextFileBrowser Component

**Location**: `SharpMUSH.Client/Components/TextFileBrowser.razor`

```razor
@inject HttpClient Http
@inject ISnackbar Snackbar

<MudTreeView T="TextFileNode" Items="@_rootNodes" @bind-SelectedValue="@_selectedNode">
    <ItemTemplate Context="item">
        <MudTreeViewItem Value="@item" Text="@item.Name" Icon="@GetIcon(item)" />
    </ItemTemplate>
</MudTreeView>

@code {
    [Parameter]
    public EventCallback<TextFileNode> OnFileSelected { get; set; }
    
    private List<TextFileNode> _rootNodes = new();
    private TextFileNode? _selectedNode;
    
    protected override async Task OnInitializedAsync()
    {
        await LoadFiles();
    }
    
    private async Task LoadFiles()
    {
        try
        {
            var response = await Http.GetFromJsonAsync<string[]>("api/textfile");
            
            if (response != null)
            {
                // Group files by directory
                var grouped = response
                    .Select(f => new TextFileNode { Name = f, IsDirectory = false })
                    .GroupBy(f => GetDirectory(f.Name))
                    .Select(g => new TextFileNode
                    {
                        Name = g.Key,
                        IsDirectory = true,
                        Children = g.ToList()
                    });
                
                _rootNodes = grouped.ToList();
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error loading files: {ex.Message}", Severity.Error);
        }
    }
    
    private string GetDirectory(string fileName)
    {
        var parts = fileName.Split('/');
        return parts.Length > 1 ? parts[0] : "root";
    }
    
    private string GetIcon(TextFileNode node)
    {
        if (node.IsDirectory)
            return Icons.Material.Filled.Folder;
        
        return node.Name.EndsWith(".md") 
            ? Icons.Material.Filled.Description 
            : Icons.Material.Filled.TextSnippet;
    }
    
    private async Task OnNodeSelected(TextFileNode node)
    {
        if (!node.IsDirectory)
        {
            await OnFileSelected.InvokeAsync(node);
        }
    }
}
```

**Model**: `SharpMUSH.Client/Models/TextFileNode.cs`
```csharp
public class TextFileNode
{
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public List<TextFileNode> Children { get; set; } = new();
}
```

#### TextFileEditor Component

**Location**: `SharpMUSH.Client/Components/TextFileEditor.razor`

```razor
@inject HttpClient Http
@inject ISnackbar Snackbar

<MudGrid>
    <MudItem xs="12">
        <MudTextField @bind-Value="@_fileName" 
                     Label="File Name" 
                     ReadOnly="@(!_isNewFile)" 
                     Variant="Variant.Outlined" />
    </MudItem>
    <MudItem xs="12" md="6">
        <MudPaper Class="pa-4">
            <MudText Typo="Typo.h6">Source</MudText>
            <MudTextField @bind-Value="@_content"
                         Lines="20"
                         Variant="Variant.Outlined"
                         FullWidth="true"
                         Immediate="true" />
        </MudPaper>
    </MudItem>
    <MudItem xs="12" md="6">
        <MudPaper Class="pa-4">
            <MudText Typo="Typo.h6">Preview</MudText>
            <MudTabs>
                <MudTabPanel Text="ANSI">
                    <pre>@_ansiPreview</pre>
                </MudTabPanel>
                <MudTabPanel Text="HTML">
                    @((MarkupString)_htmlPreview)
                </MudTabPanel>
            </MudTabs>
        </MudPaper>
    </MudItem>
    <MudItem xs="12">
        <MudButton Color="Color.Primary" Variant="Variant.Filled" OnClick="SaveFile">
            Save
        </MudButton>
        <MudButton Color="Color.Secondary" Variant="Variant.Filled" OnClick="CreateBackup">
            Backup
        </MudButton>
        <MudButton Color="Color.Default" Variant="Variant.Filled" OnClick="RefreshPreview">
            Refresh Preview
        </MudButton>
    </MudItem>
</MudGrid>

@code {
    [Parameter]
    public string? FileName { get; set; }
    
    private string _fileName = "";
    private string _content = "";
    private string _ansiPreview = "";
    private string _htmlPreview = "";
    private bool _isNewFile = false;
    
    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrEmpty(FileName))
        {
            await LoadFile(FileName);
        }
        else
        {
            _isNewFile = true;
        }
    }
    
    private async Task LoadFile(string fileName)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<TextFileResponse>($"api/textfile/{fileName}");
            
            if (response != null)
            {
                _fileName = response.FileName;
                _content = response.Content;
                await RefreshPreview();
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error loading file: {ex.Message}", Severity.Error);
        }
    }
    
    private async Task SaveFile()
    {
        try
        {
            var request = new TextFileUpdateRequest(_content);
            var response = await Http.PutAsJsonAsync($"api/textfile/{_fileName}", request);
            
            if (response.IsSuccessStatusCode)
            {
                Snackbar.Add("File saved successfully", Severity.Success);
            }
            else
            {
                Snackbar.Add("Failed to save file", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error saving file: {ex.Message}", Severity.Error);
        }
    }
    
    private async Task CreateBackup()
    {
        try
        {
            var response = await Http.PostAsync($"api/textfile/{_fileName}/backup", null);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BackupResponse>();
                Snackbar.Add($"Backup created: {result?.BackupPath}", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error creating backup: {ex.Message}", Severity.Error);
        }
    }
    
    private async Task RefreshPreview()
    {
        try
        {
            // Get ANSI preview
            var ansiResponse = await Http.PostAsJsonAsync(
                $"api/textfile/{_fileName}/render/ansi", 
                new { content = _content });
            _ansiPreview = await ansiResponse.Content.ReadAsStringAsync();
            
            // Get HTML preview
            var htmlResponse = await Http.PostAsJsonAsync(
                $"api/textfile/{_fileName}/render/html",
                new { content = _content });
            _htmlPreview = await htmlResponse.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error refreshing preview: {ex.Message}", Severity.Error);
        }
    }
}

public record TextFileResponse(string FileName, string Content);
public record BackupResponse(string BackupPath);
```

#### TextFileManagement Page

**Location**: `SharpMUSH.Client/Pages/Admin/TextFileManagement.razor`

```razor
@page "/admin/textfiles"
@attribute [Authorize(Roles = "Wizard")]

<PageTitle>Text File Management</PageTitle>

<MudGrid>
    <MudItem xs="12" md="3">
        <MudPaper Class="pa-4">
            <MudText Typo="Typo.h6">Files</MudText>
            <TextFileBrowser OnFileSelected="OnFileSelected" />
        </MudPaper>
    </MudItem>
    <MudItem xs="12" md="9">
        @if (_selectedFile != null)
        {
            <TextFileEditor FileName="@_selectedFile.Name" />
        }
        else
        {
            <MudPaper Class="pa-4">
                <MudText>Select a file to edit</MudText>
            </MudPaper>
        }
    </MudItem>
</MudGrid>

@code {
    private TextFileNode? _selectedFile;
    
    private void OnFileSelected(TextFileNode file)
    {
        _selectedFile = file;
    }
}
```

## Implementation Phases

### Phase 1: Foundation (Core Service)
**Priority**: High  
**Time Estimate**: 2-3 days

1. Create `ITextFileService` interface
2. Add `TextFileOptions` configuration
3. Implement basic `TextFileService` with file reading
4. Wire up dependency injection
5. Add unit tests for service

**Deliverables**:
- Service can read text files
- Service can index PennMUSH format files
- Configuration is working

### Phase 2: Functions and Commands
**Priority**: High  
**Time Estimate**: 1-2 days

1. Implement `textentries()` function
2. Implement `textfile()` function
3. Implement `textsearch()` function
4. Implement `help` command
5. Add function/command tests

**Deliverables**:
- All text functions working
- Help command functional
- PennMUSH compatibility maintained

### Phase 3: Markdown Rendering
**Priority**: Medium  
**Time Estimate**: 1 day

1. Integrate `MarkdownToAsciiRenderer` into service
2. Add HTML rendering capability
3. Add format detection logic
4. Support both .txt and .md files
5. Add rendering tests

**Deliverables**:
- Markdown renders to ANSI correctly
- Markdown renders to HTML correctly
- Both formats supported

### Phase 4: Web API
**Priority**: Medium  
**Time Estimate**: 1-2 days

1. Create `TextFileController`
2. Add all CRUD endpoints
3. Add security/authorization
4. Add input validation
5. Add API tests

**Deliverables**:
- REST API fully functional
- Security properly implemented
- All endpoints tested

### Phase 5: Web UI
**Priority**: Low (nice-to-have)  
**Time Estimate**: 2-3 days

1. Create `TextFileBrowser` component
2. Create `TextFileEditor` component
3. Create management page
4. Add real-time preview
5. Add UI tests

**Deliverables**:
- File browser working
- Editor with preview functional
- Admin can edit files via web

### Phase 6: Advanced Features
**Priority**: Low (optional)  
**Time Estimate**: 2-3 days

1. File watching and auto-reload
2. File versioning/history
3. Advanced search across all files
4. Audit logging
5. Performance optimization

**Deliverables**:
- Hot reload working (optional)
- File history available
- Search optimized

## Testing Strategy

### Unit Tests
- Service methods (file reading, indexing, rendering)
- Function implementations
- Configuration parsing
- File format detection

### Integration Tests
- End-to-end text file reading
- Help command with real files
- Markdown rendering pipeline
- API endpoint workflows

### UI Tests
- Component rendering
- File browser interaction
- Editor save/load
- Preview rendering

### Manual Testing
- Test with actual PennMUSH help files
- Test markdown files
- Test file editing workflow
- Test permission restrictions

## Security Considerations

### File Access Control
1. **Path Validation**: Prevent directory traversal attacks
   ```csharp
   private string ValidateAndGetPath(string fileName)
   {
       if (fileName.Contains("..") || 
           fileName.Contains("/") || 
           fileName.Contains("\\"))
       {
           throw new SecurityException("Invalid file path");
       }
       
       var fullPath = Path.Combine(_baseDirectory, fileName);
       var normalizedPath = Path.GetFullPath(fullPath);
       
       if (!normalizedPath.StartsWith(_baseDirectory))
       {
           throw new SecurityException("Path outside allowed directory");
       }
       
       return normalizedPath;
   }
   ```

2. **Permission Checks**: Require WIZARD flag for file modifications
   ```csharp
   private async Task<bool> CanEditFiles(DBRef player)
   {
       return await _permissionService.HasFlagAsync(player, "WIZARD");
   }
   ```

3. **Audit Logging**: Track all file modifications
   ```csharp
   private async Task LogFileModification(string fileName, DBRef editor, string action)
   {
       await _logger.LogInformationAsync(
           $"TextFile: {action} - File: {fileName} - Editor: {editor} - Time: {DateTime.UtcNow}");
   }
   ```

### Backup Strategy
1. Create timestamped backup before any modification
2. Keep configurable number of backups
3. Periodic cleanup of old backups
4. Restore capability

## Migration from PennMUSH

### File Format Compatibility
SharpMUSH will support the PennMUSH text file format:

```
& TOPIC NAME
This is the help text for TOPIC NAME.
It can span multiple lines.

& ANOTHER TOPIC
& ALIAS FOR TOPIC
This topic has an alias.
```

### Migration Steps
1. Copy PennMUSH help files to `text_files/help/` directory
2. Files maintain `.txt` extension
3. No code changes needed - format is compatible
4. Run `@readcache` or restart server to index

### Enhancements Over PennMUSH
1. **Markdown Support**: Create `.md` files for better formatting
2. **Web Editing**: Edit files through web interface
3. **Real-time Preview**: See rendered output while editing
4. **Version Control**: Track changes to help files
5. **Search**: Advanced search across all text files

## Performance Considerations

### Caching Strategy
1. **Startup Indexing**: Index all files when server starts
2. **In-Memory Cache**: Keep indexed entries in memory
3. **Lazy Rendering**: Render markdown on demand, cache result
4. **TTL Cache**: Expire cached renders after configurable time

### Optimization Opportunities
1. **Parallel Indexing**: Index multiple files concurrently
2. **Compressed Storage**: Compress large help files
3. **Incremental Updates**: Only re-index changed files
4. **Read-Through Cache**: Load and cache on first access

### Monitoring
1. Cache hit/miss rates
2. Indexing time on startup
3. File render time
4. API response times

## Future Enhancements

### Possible Additions
1. **Internationalization**: Multi-language help files
2. **Rich Media**: Embed images in web view
3. **Collaborative Editing**: Multiple editors with conflict resolution
4. **Templates**: Help file templates for consistency
5. **Import/Export**: Bulk import from other MUSHes
6. **AI Assistance**: Help with writing help files
7. **Full-Text Search**: Elasticsearch integration
8. **Diff View**: Compare file versions
9. **Approval Workflow**: Submit changes for review

## Conclusion

This comprehensive plan provides a roadmap for implementing text file reading and management capabilities in SharpMUSH. The phased approach allows for incremental development and testing, with core functionality delivered first and enhanced features added later.

The design leverages existing infrastructure (Helpfiles, MarkdownToAsciiRenderer, Wiki components) while adding new capabilities that enhance the MUSH experience. The result will be a modern text file system that maintains PennMUSH compatibility while offering powerful new features for content management and editing.
