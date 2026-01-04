# Text File System - Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SharpMUSH Text File System                    │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                          Client Layer                                │
├─────────────────────────────────────────────────────────────────────┤
│  Telnet Client          │         Web Client (Blazor)                │
│  ┌──────────────┐       │  ┌────────────────┐  ┌────────────────┐  │
│  │  help topic  │       │  │ TextFileBrowser│  │ TextFileEditor │  │
│  │ textentries()│       │  │  - Tree View   │  │  - Source Edit │  │
│  │ textfile()   │       │  │  - File List   │  │  - ANSI Preview│  │
│  │ textsearch() │       │  │  - Categories  │  │  - HTML Preview│  │
│  └──────────────┘       │  └────────────────┘  └────────────────┘  │
└───────┬──────────────────┴──────────┬────────────────┬──────────────┘
        │                             │                │
        │ MUSH Functions/Commands     │ HTTP REST API  │
        ▼                             ▼                │
┌─────────────────────────────────────────────────────┘
│              Application Layer
├──────────────────────────────────────────────────────────────────────┐
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │              TextFileController (REST API)                     │  │
│  │  GET  /api/textfile              - List files                 │  │
│  │  GET  /api/textfile/{file}       - Get file content           │  │
│  │  GET  /api/textfile/{file}/entries - List entries             │  │
│  │  PUT  /api/textfile/{file}       - Save file [WIZARD]         │  │
│  │  POST /api/textfile/reindex      - Reindex files [WIZARD]     │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                       │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │              Functions & Commands                              │  │
│  │  textentries(file, sep)  - List entries in file               │  │
│  │  textfile(file, entry)   - Get entry content                  │  │
│  │  textsearch(file, pattern) - Search entries                   │  │
│  │  help [topic]            - Display help for topic             │  │
│  └───────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────┬───────────────────────────────────┘
                                    │
                                    │ Uses
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Service Layer                                   │
├──────────────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                    ITextFileService                            │  │
│  │                                                                │  │
│  │  Core Operations:                                             │  │
│  │    • ListEntriesAsync(file, sep)      - Get entry list        │  │
│  │    • GetEntryAsync(file, entry)       - Get entry content     │  │
│  │    • ListFilesAsync(dir)              - List all files        │  │
│  │    • GetFileContentAsync(file)        - Get full content      │  │
│  │                                                                │  │
│  │  Search & Index:                                              │  │
│  │    • SearchEntriesAsync(file, pattern) - Search entries       │  │
│  │    • ReindexAsync()                    - Rebuild index        │  │
│  │    • GetHelpIndex()                    - Get help lookup      │  │
│  │                                                                │  │
│  │  Management:                                                  │  │
│  │    • SaveFileAsync(file, content)     - Save with backup      │  │
│  │    • DeleteFileAsync(file)            - Delete with backup    │  │
│  │    • CreateBackupAsync(file)          - Create backup         │  │
│  │                                                                │  │
│  │  Rendering:                                                   │  │
│  │    • RenderToAnsiAsync(content)       - Markdown → ANSI       │  │
│  │    • RenderToHtmlAsync(content)       - Markdown → HTML       │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                               ▲                                      │
│                               │ Implements                           │
│                               │                                      │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                    TextFileService                             │  │
│  │                                                                │  │
│  │  Components:                                                  │  │
│  │    • File Indexer    - Parses & caches file entries          │  │
│  │    • Format Detector - Identifies .txt vs .md files           │  │
│  │    • Cache Manager   - In-memory entry cache                  │  │
│  │    • File Watcher    - Optional auto-reload (dev mode)        │  │
│  │    • Security Validator - Path validation                     │  │
│  │    • Backup Manager  - Timestamped backups                    │  │
│  └───────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────┬───────────────────────────────────┘
                                    │
                                    │ Uses
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Infrastructure Layer                              │
├──────────────────────────────────────────────────────────────────────┤
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐  │
│  │   Helpfiles      │  │ MarkdownToAscii  │  │  Configuration   │  │
│  │                  │  │   Renderer       │  │                  │  │
│  │ • Index()        │  │                  │  │ TextFileOptions: │  │
│  │ • Parse & INDEX  │  │ • RenderMarkdown()│  │ • BaseDirectory  │  │
│  │   entries        │  │ • ANSI format    │  │ • HelpDir        │  │
│  │ • .txt files     │  │ • Width control  │  │ • NewsDir        │  │
│  └──────────────────┘  └──────────────────┘  │ • EnableMD       │  │
│                                               │ • CacheOnStartup │  │
│  ┌──────────────────┐  ┌──────────────────┐  │ • WatchFiles     │  │
│  │  Markdig         │  │ FileSystemWatcher│  └──────────────────┘  │
│  │  (HTML render)   │  │  (Auto-reload)   │                        │
│  └──────────────────┘  └──────────────────┘                        │
└───────────────────────────────────┬───────────────────────────────────┘
                                    │
                                    │ Reads/Writes
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Storage Layer                                 │
├──────────────────────────────────────────────────────────────────────┤
│  File System Directory Structure:                                   │
│                                                                      │
│  text_files/                         ← Base Directory               │
│  ├── help/                           ← Help Files                   │
│  │   ├── commands.txt               ← PennMUSH format              │
│  │   │   & @EMIT                    ← Index entry                  │
│  │   │   @emit <message>            ← Entry content                │
│  │   │   & @PEMIT                   ← Another entry                │
│  │   │   ...                                                        │
│  │   ├── functions.txt              ← More help                    │
│  │   └── getting-started.md         ← Markdown help                │
│  │       # Getting Started          ← Markdown format              │
│  │       Welcome to SharpMUSH...                                   │
│  │                                                                  │
│  ├── news/                           ← News Files                   │
│  │   └── announcements.txt                                         │
│  │                                                                  │
│  ├── events/                         ← Event Files                 │
│  │   └── calendar.md                                               │
│  │                                                                  │
│  └── backups/                        ← Automated Backups            │
│      └── commands_20260104_120000.txt                              │
└──────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────┐
│                        Data Flow Examples                            │
└─────────────────────────────────────────────────────────────────────┘

Example 1: help @emit
─────────────────────
User → "help @emit" → HelpCommand → TextFileService.GetHelpIndex()
                                  → Find "@EMIT" in index
                                  → Return content
                                  → (Optional) RenderToAnsi()
                                  → Display to user

Example 2: textentries(commands.txt)
────────────────────────────────────
Parser → textentries() → TextFileService.ListEntriesAsync("commands.txt")
                      → Load from cache or file
                      → Extract all & INDEX entries
                      → Return "@EMIT @PEMIT ..." (space-separated)

Example 3: textfile(commands.txt, @EMIT)
───────────────────────────────────────
Parser → textfile() → TextFileService.GetEntryAsync("commands.txt", "@EMIT")
                   → Find "@EMIT" in indexed entries
                   → Return entry content

Example 4: Web Edit File
────────────────────────
User (Web) → Load TextFileEditor → GET /api/textfile/commands.txt
                                 → TextFileController → TextFileService
                                 → Return file content + ANSI/HTML preview

Edit → Save → PUT /api/textfile/commands.txt → TextFileController
           → Validate WIZARD permission
           → TextFileService.CreateBackupAsync()
           → TextFileService.SaveFileAsync()
           → TextFileService.ReindexAsync()

Example 5: Startup Indexing
───────────────────────────
Server Start → TextFileService Constructor
            → If CacheOnStartup = true
            → Scan text_files/ directory
            → For each .txt: Use Helpfiles.Index()
            → For each .md: Index as single entry
            → Store in _indexedFiles cache
            → Ready for queries


┌─────────────────────────────────────────────────────────────────────┐
│                      Security Flow                                   │
└─────────────────────────────────────────────────────────────────────┘

Web Request
    │
    ▼
┌──────────────────┐
│  Authentication  │  ← JWT/Cookie validation
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Authorization   │  ← Check WIZARD flag or TEXT_EDIT permission
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Path Validation │  ← Prevent directory traversal
│  • No ".."       │     Validate within base directory
│  • No "/" or "\" │
│  • Normalize path│
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Create Backup   │  ← Before any modification
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  File Operation  │  ← Actual read/write
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Audit Log       │  ← Track who changed what
└──────────────────┘


┌─────────────────────────────────────────────────────────────────────┐
│                    Rendering Pipeline                                │
└─────────────────────────────────────────────────────────────────────┘

Markdown Content
    │
    ├─────────────────┐
    │                 │
    ▼                 ▼
[Telnet Client]   [Web Client]
    │                 │
    ▼                 ▼
RenderToAnsiAsync  RenderToHtmlAsync
    │                 │
    ▼                 ▼
MarkdownToAscii   Markdig HTML
Renderer          Renderer
    │                 │
    │                 │
┌───▼─────────────────▼────┐
│   Render Cache (TTL)     │
│   Key: file:format:hash  │
│   Value: rendered output │
│   TTL: 1 hour (config)   │
└──────────────────────────┘
    │
    ▼
Output to Client


┌─────────────────────────────────────────────────────────────────────┐
│                    File Format Support                               │
└─────────────────────────────────────────────────────────────────────┘

.txt Files (PennMUSH Format):          .md Files (Markdown):
─────────────────────────────           ────────────────────
& TOPIC NAME                            # Topic Name
Content for topic...                    Content with **formatting**
                                        and [links](url).
& ANOTHER TOPIC
& ALIAS                                 ## Section
More content...                         More content...

↓ Indexed as:                           ↓ Indexed as:
{                                       {
  "TOPIC NAME": "Content...",             "filename": "# Topic Name\n..."
  "ANOTHER TOPIC": "More...",           }
  "ALIAS": "More..."
}
```

## Key Design Principles

1. **Separation of Concerns**: Clear layers (Client → Application → Service → Storage)
2. **Single Responsibility**: Each component has one job
3. **Dependency Injection**: Services injected where needed
4. **Caching Strategy**: Index on startup, cache renders
5. **Security First**: Validate, authorize, audit
6. **Format Flexibility**: Support both .txt and .md
7. **Client Adaptability**: Render based on client type
8. **Backward Compatibility**: PennMUSH format supported

## Technology Stack

- **Language**: C# / .NET 10
- **Web Framework**: ASP.NET Core
- **UI Framework**: Blazor WebAssembly
- **Markdown Parser**: Markdig
- **ANSI Rendering**: Custom MarkdownToAsciiRenderer
- **Configuration**: IOptions<T> pattern
- **Logging**: ILogger<T>
- **Storage**: File System (with optional Git)

## Performance Characteristics

- **Startup**: O(n) where n = number of files (index once)
- **Help Lookup**: O(1) (dictionary lookup in cache)
- **File Read**: O(1) if cached, O(n) if not where n = file size
- **Render**: O(n) for first render, O(1) for cached
- **Search**: O(n*m) where n = files, m = average entries per file

## Scalability Considerations

- **Small Deployment** (<100 files): All in memory, no issues
- **Medium Deployment** (100-1000 files): Cache with TTL, periodic GC
- **Large Deployment** (>1000 files): Lazy loading, external search
- **Distributed**: Share file system or use database storage
