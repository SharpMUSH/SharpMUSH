# Text File System - Architectural Options Analysis

## Overview

This document presents different architectural approaches for implementing text file reading, markdown conversion, and web editing capabilities in SharpMUSH. Each approach is analyzed with pros/cons to help make informed decisions.

## 1. File Organization Strategies

### Option A: Dynamic Category Discovery (RECOMMENDED ✅)
```
text_files/
├── help/         → Auto-discovered category
├── news/         → Auto-discovered category
├── events/       → Auto-discovered category
├── policies/     → Auto-discovered category
├── custom/       → Auto-discovered category
└── any_name/     → Auto-discovered category
```

**Implementation**:
- Single base directory configured
- All subdirectories automatically discovered as categories
- Each category gets its own merged index (all files in directory)
- No limit on number of categories
- Add categories by creating directories

**Pros**:
- Maximum flexibility - unlimited categories
- Simple configuration (one base path)
- Easy to backup entire text file system
- Clear organization
- No code changes needed to add categories
- Works well with file watchers
- Self-documenting (directory = category)

**Cons**:
- All files must be under one root
- Less flexible for multiple data sources
- Need validation to prevent unwanted directories

**Recommendation**: ✅ **Best for most deployments - addresses user requirement**

### Option B: Single Directory with Hardcoded Categories (DEPRECATED)
```
text_files/
├── help/         → Configured as HelpFilesDirectory
├── news/         → Configured as NewsFilesDirectory
├── events/       → Configured as EventsFilesDirectory
```

**Pros**:
- Explicit configuration
- Easy to validate

**Cons**:
- Limited to predefined categories
- Requires code changes to add categories
- More configuration options to manage
- Not flexible

**Recommendation**: ❌ **Deprecated - use Option A instead**

### Option C: Multiple Independent Directories
```
/var/sharpmush/help/
/var/sharpmush/news/
/opt/custom/events/
/mnt/shared/documents/
```

**Pros**:
- Maximum flexibility
- Can mount different storage for different categories
- Easier to share directories across servers
- Better for distributed setups

**Cons**:
- More complex configuration
- Harder to manage backups
- More paths to validate for security
- Complexity in file watching

**Recommendation**: Use for advanced deployments with specific requirements

### Option D: Database-Backed Storage
```
TextFiles Table:
- Id (PK)
- FileName
- Category (help/news/events)
- Content
- Format (txt/md)
- CreatedBy
- CreatedAt
- UpdatedBy
- UpdatedAt
```

**Pros**:
- Built-in versioning
- Easy querying and search
- No file system concerns
- Transactional updates
- Better concurrency control

**Cons**:
- More complex to implement
- Harder to edit files directly
- Need migration tools for existing files
- May be slower for large files
- Less UNIX-friendly

**Recommendation**: Consider for future enhancement

## 2. File Format Support

### Option A: PennMUSH .txt Only
Support only the traditional PennMUSH format:
```
& HELP TOPIC
Content goes here...

& ANOTHER TOPIC
& TOPIC ALIAS
More content...
```

**Pros**:
- 100% PennMUSH compatible
- Simple to implement (leverage existing Helpfiles class)
- No format detection needed
- Easy migration

**Cons**:
- Limited formatting options
- No modern features
- Plain text only

**Recommendation**: Minimum viable product baseline

### Option B: Markdown .md Only
Support only modern markdown files:
```markdown
# Help Topic

Content with **formatting** and [links](url).

## Another Section
```

**Pros**:
- Modern, readable syntax
- Rich formatting capabilities
- Standard tooling support
- Great for new content

**Cons**:
- Not PennMUSH compatible
- Requires migration of existing files
- Indexing strategy different
- Breaking change

**Recommendation**: Not suitable as only option

### Option C: Dual Format (.txt and .md)
Support both formats with automatic detection:

**Pros**:
- Best of both worlds
- Backward compatible
- Gradual migration path
- Modern features available

**Cons**:
- More complex implementation
- Two indexing strategies
- Potential confusion about which format to use

**Recommendation**: ✅ **Best approach for flexibility**

### Option D: Hybrid (.txt with embedded markdown)
Allow markdown inside .txt files:
```
& HELP TOPIC
# This is markdown
**Bold text** in help file...
```

**Pros**:
- Single file format
- Enhanced formatting in existing structure
- Easier to implement

**Cons**:
- Non-standard approach
- May confuse tools expecting plain text
- Indexing becomes more complex
- Not truly PennMUSH compatible

**Recommendation**: Avoid - too clever

## 3. Caching and Indexing Strategies

### Option A: Metadata Indexing with FileStream Reads (RECOMMENDED ✅)
Index file metadata (path + byte positions) on startup, use FileStream + Span for reads:

```csharp
// Index entry with file position
public record IndexEntry(
    string FilePath,
    long StartPosition,
    long EndPosition,
    string EntryName
);

// On startup - build metadata index
await textFileService.IndexAllFilesAsync();

// On read - efficient FileStream + Span access
var content = await textFileService.GetEntryAsync("help/commands.txt", "@EMIT");

// Manual reload with @readcache command
@readcache
```

**Pros**:
- **Memory efficient**: Stores only metadata, not full content
- **Fast reads**: Direct seek to byte position with FileStream
- **Zero allocation**: Uses Span<byte> and ArrayPool
- **Predictable behavior**: Index built once at startup
- **Best performance**: O(1) lookup + direct file access
- **Simple implementation**: Clear separation of concerns

**Cons**:
- Changes not picked up automatically (need @readcache)
- Requires restart or manual reload
- Not ideal for development without file watching

**Recommendation**: ✅ **Best for production - combines speed with memory efficiency**

### Option B: Full Content Indexing
Store complete file content in memory:

```csharp
// Index entry with full content
private Dictionary<string, Dictionary<string, string>> _contentCache;

// On startup
await textFileService.IndexAllFilesAsync();

// Manual reload
@readcache command -> textFileService.ReindexAsync()
```

**Pros**:
- Fastest possible reads (already in memory)
- Simple retrieval logic
- No file I/O on reads

**Cons**:
- **High memory usage**: Stores all content in RAM
- Scales poorly with large file sets
- Wasteful for infrequently accessed content
- Changes not picked up automatically

**Recommendation**: Only for very small deployments (<1MB total text)

### Option C: File System Watching with Auto-Reload
Watch files and automatically reload when changed:

```csharp
var watcher = new FileSystemWatcher(path);
watcher.Changed += async (s, e) => await ReindexFileAsync(e.Name);
watcher.EnableRaisingEvents = true;
```

**Pros**:
- Changes picked up immediately
- Great for development
- No manual intervention needed
- Better user experience

**Cons**:
- File system monitoring overhead
- Potential race conditions
- May trigger multiple times for single change
- Complex error handling

**Recommendation**: ✅ **Best for development** (configurable option)

### Option C: Lazy Loading with TTL Cache
Load files on first access, cache for configured time:

```csharp
var cached = _cache.GetOrAdd(fileName, 
    () => LoadFile(fileName),
    TimeSpan.FromMinutes(30));
```

**Pros**:
- Faster startup
- Memory efficient for large file sets
- Only loads what's needed
- Configurable cache lifetime

**Cons**:
- First access slower
- More complex cache invalidation
- Inconsistent performance
- TTL may be wrong

**Recommendation**: Consider for very large installations

### Option D: Hybrid (Startup + Watch + TTL)
Combine approaches based on configuration:

```csharp
// Startup: Index high-priority files (help)
await IndexHelpFilesAsync();

// Watch: Monitor for changes
if (config.WatchForChanges) EnableFileWatcher();

// TTL: Lazy load others with expiration
var news = await GetOrLoadWithTTL("news/file.txt");
```

**Pros**:
- Maximum flexibility
- Optimized per use case
- Best performance overall

**Cons**:
- Most complex to implement
- More configuration options
- Harder to reason about

**Recommendation**: Future optimization

## 4. Markdown Rendering Approaches

### Option A: Always Render to ANSI
Convert all markdown to ANSI regardless of client:

**Pros**:
- Consistent experience
- Single code path
- Existing infrastructure (MarkdownToAsciiRenderer)

**Cons**:
- Web clients don't benefit
- ANSI in HTML looks bad
- Lost opportunity for rich web UI

**Recommendation**: Baseline approach

### Option B: Render Based on Client Type
Detect client and render appropriately:

```csharp
if (client.IsWebClient)
    return await RenderToHtmlAsync(markdown);
else if (client.SupportsPueblo)
    return await RenderToPuebloAsync(markdown);
else
    return await RenderToAnsiAsync(markdown);
```

**Pros**:
- Best experience per client
- Leverages client capabilities
- Modern web experience

**Cons**:
- More rendering code
- Client detection needed
- Multiple pipelines to maintain

**Recommendation**: ✅ **Best user experience**

### Option C: Store Multiple Rendered Formats
Pre-render and cache all formats:

```csharp
class RenderedContent
{
    string PlainText { get; set; }
    string AnsiText { get; set; }
    string HtmlText { get; set; }
    string PuebloText { get; set; }
}
```

**Pros**:
- Fastest delivery
- No runtime rendering
- Consistent output

**Cons**:
- More storage
- More complex updates
- Cache invalidation
- Memory overhead

**Recommendation**: Optimization for high-traffic

### Option D: Render On-Demand with Cache
Render as needed, cache results:

```csharp
var ansiKey = $"{fileName}:ansi";
var cached = _renderCache.GetOrAdd(ansiKey,
    () => RenderToAnsi(content),
    TimeSpan.FromHours(1));
```

**Pros**:
- Balance of performance and memory
- Only render what's needed
- Reasonable cache overhead

**Cons**:
- First render slower
- Cache management
- Memory can grow

**Recommendation**: ✅ **Best balance**

## 5. Web Editing Security Models

### Option A: Wizard-Only
Only players with WIZARD flag can edit:

```csharp
[Authorize(Roles = "Wizard")]
public async Task<IActionResult> SaveFile(...)
{
    // Save file
}
```

**Pros**:
- Simple to implement
- Clear security boundary
- Matches MUSH tradition

**Cons**:
- Inflexible
- Can't delegate to trusted users
- All-or-nothing

**Recommendation**: Minimum viable security

### Option B: Permission-Based
Define specific text file permissions:

```csharp
[Authorize(Policy = "CanEditTextFiles")]
public async Task<IActionResult> SaveFile(...)
{
    if (!await HasPermission(user, "TEXT_FILE_EDIT"))
        return Forbid();
}
```

**Pros**:
- Granular control
- Can delegate to non-wizards
- Better security model
- Audit trail per permission

**Cons**:
- More complex
- Need permission system
- Configuration overhead

**Recommendation**: ✅ **Better security model**

### Option C: Per-Category Permissions
Different permissions for different file types:

```csharp
var category = GetCategory(fileName); // "help", "news", etc.
var permission = $"EDIT_{category.ToUpper()}_FILES";

if (!await HasPermission(user, permission))
    return Forbid();
```

**Pros**:
- Very granular
- Can have help editors, news editors, etc.
- Matches organizational structure

**Cons**:
- Complex permission matrix
- More configuration
- Potential confusion

**Recommendation**: Advanced feature

### Option D: Approval Workflow
Edits require approval before going live:

```csharp
// Save to pending
await SavePendingEdit(fileName, content, editor);

// Notify approvers
await NotifyApprovers(fileName, editor);

// Later: approve or reject
await ApproveEdit(fileName, approver);
```

**Pros**:
- Quality control
- Multiple reviewers
- Prevents mistakes
- Professional workflow

**Cons**:
- Much more complex
- Needs pending system
- Notification system
- UI complexity

**Recommendation**: Future enhancement

## 6. Backup and Versioning Strategies

### Option A: Simple Timestamped Backups
Create backup with timestamp before each edit:

```csharp
var backup = $"{fileName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
File.Copy(original, backup);
```

**Pros**:
- Simple to implement
- Easy to restore
- Clear audit trail
- No database needed

**Cons**:
- Can accumulate many files
- No automatic cleanup
- Hard to compare versions
- Storage overhead

**Recommendation**: ✅ **Good starting point**

### Option B: Git Integration
Use Git for version control:

```csharp
await _gitService.CommitAsync(fileName, $"Edited by {editor}");
```

**Pros**:
- Professional version control
- Built-in diff/merge
- Standard tooling
- Branch/tag support

**Cons**:
- Git dependency
- More complex
- Learning curve
- Potential conflicts

**Recommendation**: Excellent for serious deployments

### Option C: Database Versioning
Store versions in database:

```sql
CREATE TABLE TextFileVersions (
    Id INT PRIMARY KEY,
    FileName VARCHAR(255),
    Version INT,
    Content TEXT,
    EditedBy INT,
    EditedAt DATETIME,
    Comment TEXT
);
```

**Pros**:
- Query history easily
- Structured data
- Transactional
- Easy to display in UI

**Cons**:
- Database overhead
- Schema management
- Migration needed
- Storage in database

**Recommendation**: Good for database-first approach

### Option D: Hybrid (Backup + Git)
Simple backups for safety, Git for history:

```csharp
// Always create immediate backup
await CreateBackupAsync(fileName);

// If Git enabled, also commit
if (_config.UseGit)
    await _gitService.CommitAsync(fileName, message);
```

**Pros**:
- Belt and suspenders
- Best of both worlds
- Flexibility

**Cons**:
- Most complex
- Duplicate storage
- Configuration

**Recommendation**: Enterprise option

## 7. Search Implementation Options

### Option A: Simple String Matching
Search file content with string.Contains():

```csharp
var matches = files
    .Where(f => f.Content.Contains(searchTerm, 
        StringComparison.OrdinalIgnoreCase))
    .Select(f => f.Name);
```

**Pros**:
- Simple to implement
- No dependencies
- Fast for small sets

**Cons**:
- Slow for large files
- No ranking
- Limited features
- No fuzzy matching

**Recommendation**: Baseline implementation

### Option B: Regex Search
Use regular expressions for pattern matching:

```csharp
var regex = new Regex(pattern, RegexOptions.IgnoreCase);
var matches = files
    .Where(f => regex.IsMatch(f.Content))
    .Select(f => f.Name);
```

**Pros**:
- Powerful patterns
- Built-in .NET
- Good for tech users

**Cons**:
- Complex for users
- Performance issues
- Security concerns (ReDoS)

**Recommendation**: Advanced option

### Option C: Full-Text Search (Lucene.NET)
Use Lucene.NET for indexing and search:

```csharp
var searcher = new IndexSearcher(directory);
var query = parser.Parse(searchTerm);
var hits = searcher.Search(query, 10);
```

**Pros**:
- Fast full-text search
- Ranking/scoring
- Fuzzy matching
- Faceting

**Cons**:
- External dependency
- Index maintenance
- Complexity
- Storage overhead

**Recommendation**: For large installations

### Option D: External Search (Elasticsearch)
Integrate with Elasticsearch:

```csharp
var response = await _elasticClient.SearchAsync<TextFile>(s => s
    .Query(q => q.Match(m => m.Field(f => f.Content).Query(searchTerm)))
);
```

**Pros**:
- Enterprise-grade
- Distributed
- Analytics
- Very fast

**Cons**:
- External service required
- Network overhead
- Complex setup
- Overkill for most

**Recommendation**: Only for very large deployments

## Recommended Architecture

Based on analysis, here's the recommended baseline architecture:

### File Organization
- **Single directory with subdirectories** (Option A)
- Base path: `text_files/`
- Subdirectories: `help/`, `news/`, `events/`, `backups/`

### File Format
- **Dual format support** (Option C)
- `.txt` for PennMUSH compatibility
- `.md` for new markdown content
- Auto-detect based on extension

### Caching/Indexing
- **Startup indexing** (Option A) for production
- **File watching** (Option B) as configurable dev option
- Configuration flag to enable watching

### Rendering
- **Render based on client** (Option B)
- ANSI for telnet clients
- HTML for web clients
- **Cache rendered output** (Option D)
- TTL-based cache invalidation

### Security
- **Permission-based** (Option B)
- Start with wizard-only
- Add granular permissions later
- Path validation always enabled

### Backup
- **Simple timestamped backups** (Option A)
- Optional Git integration (Option B)
- Configurable retention policy
- Automatic cleanup of old backups

### Search
- **Simple string matching** (Option A) initially
- **Regex support** (Option B) as enhancement
- Plan for Lucene.NET (Option C) if needed

## Implementation Priority

1. **Phase 1 - MVP**: Options A for all categories
2. **Phase 2 - Enhancement**: Add configurable options
3. **Phase 3 - Advanced**: Git, permissions, full-text search
4. **Phase 4 - Enterprise**: Elasticsearch, approval workflow, advanced versioning

## Configuration Example

```yaml
TextFileOptions:
  TextFilesDirectory: "text_files"
  HelpFilesDirectory: "help"
  NewsFilesDirectory: "news"
  EventsFilesDirectory: "events"
  EnableMarkdownRendering: true
  CacheOnStartup: true
  WatchForChanges: false  # true for dev
  BackupEnabled: true
  BackupDirectory: "backups"
  BackupRetentionDays: 30
  UseGit: false  # optional
  RenderCacheTTL: 3600  # seconds
  SecurityModel: "Permission"  # Wizard or Permission
```

## Conclusion

This analysis provides multiple approaches for each aspect of the text file system. The recommended architecture balances:
- **Simplicity**: Easy to implement and maintain
- **Compatibility**: PennMUSH migration path
- **Flexibility**: Room to grow and enhance
- **Performance**: Good enough for most deployments
- **Security**: Proper validation and permissions

Start with the recommended baseline and enhance based on actual needs and usage patterns.
