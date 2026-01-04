# Text File System - Implementation Summary

## Overview

This directory contains comprehensive planning documentation for implementing text file reading, markdown conversion, and web editing capabilities in SharpMUSH. These documents address the requirements for the `textentries()` function, `help` command, and web-based file management.

## Planning Documents

### 1. TEXT_FILE_SYSTEM_PLAN.md (Primary Reference)
**Purpose**: Complete implementation plan with detailed specifications

**Contents**:
- Full service interface design (`ITextFileService`)
- Configuration schema (`TextFileOptions`)
- Complete code implementations for:
  - `textentries()`, `textfile()`, `textsearch()` functions
  - `help` command
  - Web API controller
  - Blazor UI components
- Security model and validation
- Backup and versioning strategies
- Testing approach
- Performance considerations
- Migration path from PennMUSH

**Use this when**: You need detailed specifications, complete code examples, or understanding the full scope of the system.

**Size**: ~1,125 lines of detailed documentation

### 2. TEXT_FILE_ARCHITECTURE_OPTIONS.md (Decision Guide)
**Purpose**: Analysis of architectural approaches with pros/cons

**Contents**:
- 7 major architectural decision areas:
  1. File Organization (single dir vs multiple vs database)
  2. File Format (txt only vs md only vs dual support)
  3. Caching/Indexing (startup vs watching vs lazy)
  4. Markdown Rendering (always ANSI vs client-based vs cached)
  5. Security (wizard-only vs permissions vs approval workflow)
  6. Backup/Versioning (simple vs Git vs database)
  7. Search (string matching vs regex vs Lucene vs Elasticsearch)
- Multiple options per area with trade-offs
- Recommended baseline architecture
- Configuration examples
- Implementation priorities

**Use this when**: Making architectural decisions, understanding trade-offs, or customizing the implementation for specific needs.

**Size**: ~510 lines of decision analysis

### 3. TEXT_FILE_QUICK_START.md (Implementation Guide)
**Purpose**: Step-by-step implementation reference

**Contents**:
- Phase-by-phase implementation steps
- Complete code snippets ready to use
- Directory structure setup
- Example help files (PennMUSH and markdown formats)
- Testing procedures
- Common issues and solutions
- Configuration checklist
- Time estimates

**Use this when**: Actually implementing the system, setting up test files, or troubleshooting issues.

**Size**: ~480 lines of practical guidance

## Quick Reference

### What Problem Does This Solve?

The `textentries()` function and `help` command need to:
1. Read text files from disk
2. Parse PennMUSH-format index entries
3. Support markdown rendering to ANSI/HTML
4. Enable web-based editing of help files

### Recommended Approach

**Phase 1 (MVP)**: Core service for reading files
- Single directory with subdirectories
- Support both .txt (PennMUSH) and .md (markdown) files
- Index on startup, manual reload
- Functions: `textentries()`, `textfile()`, `textsearch()`
- Command: `help`

**Phase 2**: Markdown rendering
- ANSI output for telnet clients
- HTML output for web clients
- TTL-based render cache

**Phase 3**: Web editing
- REST API for file operations
- Blazor components for browsing/editing
- WIZARD-only permissions
- Timestamped backups

### Key Design Decisions

| Area | Decision | Rationale |
|------|----------|-----------|
| File Organization | Single directory with subdirs | Simple, maintainable, easy backup |
| File Format | Dual (.txt + .md) | PennMUSH compatible + modern features |
| Caching | Startup index + optional watch | Fast, predictable, dev-friendly option |
| Rendering | Client-based with cache | Best UX per client type |
| Security | Permission-based | Flexible, granular control |
| Backup | Timestamped files | Simple, reliable, easy restore |
| Search | String → Regex → Lucene | Progressive enhancement |

### Implementation Time Estimates

| Phase | Description | Time |
|-------|-------------|------|
| Phase 1 | Core service + config | 1-2 days |
| Phase 2 | Functions + commands | 1 day |
| Phase 3 | Markdown rendering | 1 day |
| Phase 4 | Web API | 1-2 days |
| Phase 5 | Web UI | 2-3 days |
| **MVP** | Phases 1-2 only | **1-2 days** |
| **Full** | All phases | **5-10 days** |

## How to Use These Documents

### For Planning/Review
1. Read this summary
2. Review `TEXT_FILE_ARCHITECTURE_OPTIONS.md` for decision rationale
3. Check `TEXT_FILE_SYSTEM_PLAN.md` for complete scope

### For Implementation
1. Start with `TEXT_FILE_QUICK_START.md`
2. Reference `TEXT_FILE_SYSTEM_PLAN.md` for detailed code examples
3. Consult `TEXT_FILE_ARCHITECTURE_OPTIONS.md` when customizing

### For Specific Tasks

**Setting up directories**:
→ See "Directory Structure" in QUICK_START.md

**Writing service interface**:
→ See "Service Interface Design" in PLAN.md

**Implementing functions**:
→ See "Function Implementations" in PLAN.md or Phase 2 in QUICK_START.md

**Making architectural decisions**:
→ See relevant section in ARCHITECTURE_OPTIONS.md

**Creating help files**:
→ See examples in QUICK_START.md

**Adding web editing**:
→ See "Web UI Components" in PLAN.md

**Security implementation**:
→ See "Security Considerations" in PLAN.md

## File Organization Example

After implementation, your project will have:

```
SharpMUSH/
├── text_files/                    # New directory for text files
│   ├── help/                      # Category: "help" (auto-discovered)
│   │   ├── commands.txt          # PennMUSH format help
│   │   ├── functions.txt
│   │   └── getting-started.md    # Markdown help
│   ├── news/                      # Category: "news" (auto-discovered)
│   │   └── announcements.txt
│   ├── events/                    # Category: "events" (auto-discovered)
│   │   └── calendar.md
│   ├── policies/                  # Category: "policies" (custom - auto-discovered)
│   │   └── rules.md
│   └── backups/                  # Automated backups (not a category)
│
├── SharpMUSH.Library/
│   └── Services/Interfaces/
│       └── ITextFileService.cs   # New interface (supports dynamic categories)
│   │   ├── functions.txt
│   │   └── getting-started.md    # Markdown help
│   ├── news/
│   │   └── announcements.txt
│   ├── events/
│   │   └── calendar.md
│   └── backups/                  # Automated backups
│
├── SharpMUSH.Library/
│   └── Services/Interfaces/
│       └── ITextFileService.cs   # New interface
│
├── SharpMUSH.Configuration/
│   └── Options/
│       └── TextFileOptions.cs    # New configuration
│
├── SharpMUSH.Implementation/
│   ├── Services/
│   │   └── TextFileService.cs    # New service
│   ├── Functions/
│   │   └── TextFunctions.cs      # Updated functions
│   └── Commands/
│       └── GeneralCommands.cs    # Added help command
│
├── SharpMUSH.Server/
│   └── Controllers/
│       └── TextFileController.cs # New API
│
└── SharpMUSH.Client/
    ├── Components/
    │   ├── TextFileBrowser.razor # New component
    │   └── TextFileEditor.razor  # New component
    └── Pages/Admin/
        └── TextFileManagement.razor # New page
```

## Core Interfaces

### ITextFileService
The main service interface providing:
- `ListEntriesAsync()` - Get all entries in a file
- `GetEntryAsync()` - Get specific entry content
- `ListFilesAsync()` - List all text files
- `GetFileContentAsync()` - Get full file content
- `SearchEntriesAsync()` - Search entries by pattern
- `SaveFileAsync()` - Save file (with backup)
- `ReindexAsync()` - Rebuild index
- `RenderToAnsiAsync()` - Render markdown to ANSI
- `RenderToHtmlAsync()` - Render markdown to HTML

### Functions
- `textentries(file, [separator])` - List entries in file
- `textfile(file, entry)` - Get entry content
- `textsearch(file, pattern, [separator])` - Search entries

### Commands
- `help [topic]` - Display help for topic

## Compatibility Notes

### PennMUSH Migration
SharpMUSH maintains full compatibility with PennMUSH text file format:

```
& TOPIC NAME
Content for the topic...

& ANOTHER TOPIC
& ALIAS
Content with an alias...
```

**Migration Steps**:
1. Copy PennMUSH help files to `text_files/help/`
2. Keep `.txt` extension
3. No code changes needed
4. Restart server or use `@readcache` to index

### Enhancements Over PennMUSH
1. **Markdown files** - Create `.md` files for better formatting
2. **Web editing** - Edit through web interface
3. **Live preview** - See rendered output while editing
4. **Versioning** - Track file changes over time
5. **Advanced search** - Search across all text files

## Testing Checklist

### Service Tests
- [ ] Can index .txt files with PennMUSH format
- [ ] Can index .md files as single entries
- [ ] Can list all entries in a file
- [ ] Can retrieve specific entry
- [ ] Can list all files in directory
- [ ] Can reindex after changes
- [ ] Returns correct help index

### Function Tests
- [ ] `textentries()` returns space-separated list
- [ ] `textentries()` accepts custom separator
- [ ] `textfile()` retrieves correct entry
- [ ] `textfile()` returns error for missing file
- [ ] `textfile()` returns error for missing entry
- [ ] `textsearch()` finds matching entries

### Command Tests
- [ ] `help` without argument shows usage
- [ ] `help <topic>` displays correct content
- [ ] `help` handles missing topics gracefully
- [ ] Help content is rendered if markdown enabled

### Integration Tests
- [ ] Service loads files on startup if configured
- [ ] Web API endpoints require authentication
- [ ] File paths are validated for security
- [ ] Backups are created before edits
- [ ] Changes are logged for audit

## Security Considerations

### Critical Security Measures
1. **Path Validation**: Prevent directory traversal
   ```csharp
   if (fileName.Contains("..")) throw new SecurityException();
   ```

2. **Permission Checks**: Require WIZARD flag
   ```csharp
   if (!await HasPermission(user, "WIZARD")) return Forbid();
   ```

3. **Input Sanitization**: Validate all file names
   ```csharp
   var normalized = Path.GetFullPath(path);
   if (!normalized.StartsWith(baseDir)) throw new SecurityException();
   ```

4. **Audit Logging**: Track all modifications
   ```csharp
   await LogFileEdit(fileName, editor, timestamp);
   ```

## Configuration Example

Add to `mushcnf.dst`:

```
# Text File System Configuration
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

## Next Steps

1. **Review** these planning documents
2. **Decide** which phases to implement
3. **Start** with Phase 1 (MVP) from QUICK_START.md
4. **Test** thoroughly at each phase
5. **Extend** with additional phases as needed

## Document Maintenance

### When to Update
- Architecture changes significantly
- New features added to the plan
- Implementation reveals better approaches
- User feedback suggests improvements

### How to Update
1. Update primary document (PLAN.md)
2. Reflect changes in ARCHITECTURE_OPTIONS.md if architectural
3. Update QUICK_START.md if it affects implementation
4. Update this summary with any structural changes

## Additional Resources

### Existing Code to Reference
- `SharpMUSH.Documentation/Helpfiles.cs` - PennMUSH indexing
- `SharpMUSH.Documentation/MarkdownToAsciiRenderer/` - ANSI rendering
- `SharpMUSH.Implementation/Functions/MarkdownFunctions.cs` - Markdown rendering
- `SharpMUSH.Client/Components/WikiEdit.razor` - Edit component pattern

### External Documentation
- [PennMUSH Help System](https://github.com/pennmush/pennmush/wiki/Help-System)
- [CommonMark Spec](https://commonmark.org/)
- [Markdig (Markdown library)](https://github.com/xoofx/markdig)

## Questions?

For questions about:
- **Architecture**: See ARCHITECTURE_OPTIONS.md
- **Implementation**: See QUICK_START.md
- **Detailed specs**: See PLAN.md
- **Quick overview**: This document

## Summary

Three comprehensive planning documents provide everything needed to implement text file reading, markdown rendering, and web editing in SharpMUSH:

1. **PLAN.md** - What to build (specifications)
2. **ARCHITECTURE_OPTIONS.md** - How to build it (decisions)
3. **QUICK_START.md** - Step-by-step implementation

Total planning: ~2,300 lines of documentation covering architecture, implementation, testing, security, and migration.

**MVP can be implemented in 1-2 days. Full system in 5-10 days.**

Ready for implementation! 🚀
