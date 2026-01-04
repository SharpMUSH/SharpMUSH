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
│  │  textentries(ref, sep)   - List entries (ref = "file" or    │  │
│  │                            "category/file")                  │  │
│  │  textfile(ref, entry)    - Get entry content                │  │
│  │  textsearch(ref, pattern) - Search entries                  │  │
│  │  help [topic]            - Search all categories for topic  │  │
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
│  │  Category Management:                                         │  │
│  │    • ListCategoriesAsync()            - List all categories   │  │
│  │                                                                │  │
│  │  Core Operations (supports "file" or "category/file"):       │  │
│  │    • ListEntriesAsync(ref, sep)       - Get entry list        │  │
│  │    • GetEntryAsync(ref, entry)        - Get entry content     │  │
│  │    • ListFilesAsync(category)         - List files            │  │
│  │    • GetFileContentAsync(ref)         - Get full content      │  │
│  │                                                                │  │
│  │  Search & Index:                                              │  │
│  │    • SearchEntriesAsync(ref, pattern) - Search entries        │  │
│  │    • ReindexAsync()                   - Rebuild all indexes   │  │
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
│  │    • Category Scanner   - Auto-discovers subdirectories       │  │
│  │    • File Indexer       - Parses & caches per-category        │  │
│  │    • Format Detector    - Identifies .txt vs .md files        │  │
│  │    • Cache Manager      - Per-category merged indexes         │  │
│  │    • File Watcher       - Optional auto-reload (dev mode)     │  │
│  │    • Security Validator - Path validation                     │  │
│  │    • Backup Manager     - Timestamped backups                 │  │
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
│  │   entries        │  │ • ANSI format    │  │   (auto-discover │  │
│  │ • .txt files     │  │ • Width control  │  │    categories)   │  │
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
│  File System Directory Structure (Dynamic Category Discovery):      │
│                                                                      │
│  text_files/                         ← Base Directory               │
│  ├── help/                           ← Category: "help" (auto)      │
│  │   ├── commands.txt               ← PennMUSH format              │
│  │   │   & @EMIT                    ← Index entry                  │
│  │   │   @emit <message>            ← Entry content                │
│  │   │   & @PEMIT                   ← Another entry                │
│  │   │   ...                                                        │
│  │   ├── functions.txt              ← More help                    │
│  │   └── getting-started.md         ← Markdown help                │
│  │       # Getting Started          ← Markdown format              │
│  │       Welcome to SharpMUSH...                                   │
│  │       → All merged into ONE "help" category index               │
│  │                                                                  │
│  ├── news/                           ← Category: "news" (auto)      │
│  │   └── announcements.txt                                         │
│  │                                                                  │
│  ├── events/                         ← Category: "events" (auto)   │
│  │   └── calendar.md                                               │
│  │                                                                  │
│  ├── policies/                       ← Category: "policies" (auto) │
│  │   └── rules.md                   ← Any category name works!    │
│  │                                                                  │
│  └── backups/                        ← Automated Backups (ignored) │
│      └── commands_20260104_120000.txt                              │
│                                                                      │
│  Note: Any subdirectory = category with merged index from all files│
└──────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────┐
│                        Data Flow Examples                            │
└─────────────────────────────────────────────────────────────────────┘

Example 1: help @emit (searches all categories)
────────────────────────────────────────────────
User → "help @emit" → HelpCommand → TextFileService.GetEntryAsync("*", "@EMIT")
                                  → Search all category indexes
                                  → Find "@EMIT" in "help" category
                                  → Return content
                                  → (Optional) RenderToAnsi()
                                  → Display to user

Example 2: textentries(commands.txt) (searches all categories)
───────────────────────────────────────────────────────────────
Parser → textentries() → TextFileService.ListEntriesAsync("commands.txt")
                      → Search all categories for "commands.txt"
                      → Find in "help" category
                      → Extract all & INDEX entries
                      → Return "@EMIT @PEMIT ..." (space-separated)

Example 3: textentries(help/commands.txt) (specific category)
──────────────────────────────────────────────────────────────
Parser → textentries() → TextFileService.ListEntriesAsync("help/commands.txt")
                      → Parse "help/commands.txt" → category="help", file="commands.txt"
                      → Look only in "help" category
                      → Extract all & INDEX entries
                      → Return "@EMIT @PEMIT ..." (space-separated)

Example 4: textfile(commands.txt, @EMIT) (searches all categories)
───────────────────────────────────────────────────────────────────
Parser → textfile() → TextFileService.GetEntryAsync("commands.txt", "@EMIT")
                   → Search all categories for file
                   → Find in "help" category index
                   → Return entry content

Example 5: Web Edit File
────────────────────────
User (Web) → Load TextFileEditor → GET /api/textfile/help/commands.txt
                                 → TextFileController → TextFileService
                                 → Return file content + ANSI/HTML preview

Edit → Save → PUT /api/textfile/help/commands.txt → TextFileController
           → Validate WIZARD permission
           → TextFileService.CreateBackupAsync()
           → TextFileService.SaveFileAsync()
           → TextFileService.ReindexAsync() (rebuild "help" category index)

Example 6: Startup Indexing (Dynamic Category Discovery)
──────────────────────────────────────────────────────────
Server Start → TextFileService Constructor
            → If CacheOnStartup = true
            → Scan text_files/ for subdirectories
            → For each subdirectory (category):
               → Scan all files in category
               → For .txt: Use Helpfiles.Index() to get entries
               → For .md: Index as single entry
               → Merge all entries into ONE category index
            → Store in _categoryIndexes[categoryName]
            → Ready for queries (searches all categories or specific)


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
