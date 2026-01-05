# PennMUSH Implementation Comparison

**Generated:** 2026-01-05

This document tracks the implementation status of PennMUSH commands and functions in SharpMUSH.

## Summary

| Category | PennMUSH | SharpMUSH | Missing | Coverage |
|----------|----------|-----------|---------|----------|
| **Commands** | 159 | 171 | 4 | 97% |
| **Functions** | 513 | 526 | 35 | 93% |

## Missing Commands

The following PennMUSH commands are not yet implemented in SharpMUSH:

- [ ] `@ELOCK`
- [ ] `@EUNLOCK`
- [ ] `@ULOCK`
- [ ] `@UUNLOCK`

## Missing Functions

The following PennMUSH functions are not yet implemented in SharpMUSH:

- [ ] `ATTRCNT()`
- [ ] `ATTRPCNT()`
- [ ] `AVG()`
- [ ] `CNAME()`
- [ ] `CONFIG()`
- [ ] `CONVSECS()`
- [ ] `CONVTIME()`
- [ ] `CONVUTCSECS()`
- [ ] `CONVUTCTIME()`
- [ ] `DELETE()`
- [ ] `DYNHELP()`
- [ ] `ELEMENT()`
- [ ] `EXP()`
- [ ] `HOSTNAME()`
- [ ] `IDLESECS()`
- [ ] `INSERT()`
- [ ] `LCSTR2()`
- [ ] `MOD()`
- [ ] `MODULUS()`
- [ ] `NCAND()`
- [ ] `PARSE()`
- [ ] `PICKRAND()`
- [ ] `REGLMATCHALLI()`
- [ ] `REPLACE()`
- [ ] `REVERSE()`
- [ ] `RNUM()`
- [ ] `SEARCH()`
- [ ] `SHA0()`
- [ ] `SOUNDLIKE()`
- [ ] `SPEAKPENN()`
- [ ] `STATS()`
- [ ] `U()`
- [ ] `UCSTR2()`
- [ ] `VAL()`
- [ ] `XMWHO()`

## SharpMUSH-Specific Commands

These commands are implemented in SharpMUSH but not found in PennMUSH source:

- `&` - SharpMUSH extension
- `@MAP` - SharpMUSH extension
- `CONNECT` - SharpMUSH extension
- `DESERT` - SharpMUSH extension
- `DISMISS` - SharpMUSH extension
- `EMPTY` - SharpMUSH extension
- `GOTO` - SharpMUSH extension
- `HUH_COMMAND` - SharpMUSH extension
- `QUIT` - SharpMUSH extension
- `SCORE` - SharpMUSH extension
- `SEMIPOSE` - SharpMUSH extension
- `SESSION` - SharpMUSH extension
- `UNIMPLEMENTED_COMMAND` - SharpMUSH extension
- `WARN_ON_MISSING` - SharpMUSH extension
- `WITH` - SharpMUSH extension
- `]` - SharpMUSH extension

## SharpMUSH-Specific Functions

These functions are implemented in SharpMUSH but not found in PennMUSH:

### Channel Functions

- `CBUFFER()` - SharpMUSH extension
- `CBUFFERADD()` - SharpMUSH extension
- `CDESC()` - SharpMUSH extension
- `CEMIT()` - SharpMUSH extension
- `CFLAGS()` - SharpMUSH extension
- `CHANNELS()` - SharpMUSH extension
- `CLFLAGS()` - SharpMUSH extension
- `CLOCK()` - SharpMUSH extension
- `CMOGRIFIER()` - SharpMUSH extension
- `CMSGS()` - SharpMUSH extension
- `COWNER()` - SharpMUSH extension
- `CRECALL()` - SharpMUSH extension
- `CSTATUS()` - SharpMUSH extension
- `CTITLE()` - SharpMUSH extension
- `CUSERS()` - SharpMUSH extension
- `CWHO()` - SharpMUSH extension

### Mail Functions

- `MAIL()` - SharpMUSH extension
- `MAILDSTATS()` - SharpMUSH extension
- `MAILFROM()` - SharpMUSH extension
- `MAILFSTATS()` - SharpMUSH extension
- `MAILLIST()` - SharpMUSH extension
- `MAILSEND()` - SharpMUSH extension
- `MAILSTATS()` - SharpMUSH extension
- `MAILSTATUS()` - SharpMUSH extension
- `MAILSUBJECT()` - SharpMUSH extension
- `MAILTIME()` - SharpMUSH extension

### Web/HTML Functions

- `DECOMPOSEWEB()` - SharpMUSH extension
- `ENDTAG()` - SharpMUSH extension
- `FORMDECODE()` - SharpMUSH extension
- `HTML()` - SharpMUSH extension
- `PUEBLO()` - SharpMUSH extension
- `RENDERMARKDOWN()` - SharpMUSH extension
- `RENDERMARKDOWNCUSTOM()` - SharpMUSH extension
- `TAG()` - SharpMUSH extension
- `TAGWRAP()` - SharpMUSH extension
- `WSHTML()` - SharpMUSH extension
- `WSJSON()` - SharpMUSH extension

### Other Functions

- `ATTRIB_SET#()` - SharpMUSH extension
- `CNAND()` - SharpMUSH extension
- `DOWNMOTD()` - SharpMUSH extension
- `FOLDERSTATS()` - SharpMUSH extension
- `FULLMOTD()` - SharpMUSH extension
- `MALIAS()` - SharpMUSH extension
- `MOTD()` - SharpMUSH extension
- `NSCEMIT()` - SharpMUSH extension
- `REGMATCHALLI()` - SharpMUSH extension
- `WIZMOTD()` - SharpMUSH extension
- `ZFIND()` - SharpMUSH extension

## Notes

- **Missing Items**: Functions/commands present in PennMUSH but not in SharpMUSH
- **SharpMUSH Extensions**: Additional functionality specific to SharpMUSH, including:
  - Enhanced channel system with additional query functions
  - Integrated mail system (PennMUSH uses @mail command only)
  - Web/HTML utilities for rendering and markup
  - Markdown rendering support
- **Comparison Source**: Based on PennMUSH source code and SharpMUSH implementation
- **Last Updated**: This comparison should be run periodically as both codebases evolve

## Recommendations

1. **High Priority**: Implement missing core functions used frequently in MUSH code:
   - `U()` - Call user-defined function (critical)
   - `ELEMENT()` - Extract element from list
   - `INSERT()` / `DELETE()` / `REPLACE()` - List manipulation
   - `PARSE()` - Parse and evaluate code
   - `SEARCH()` - Search for objects

2. **Medium Priority**: Lock and utility functions:
   - `@ELOCK` / `@EUNLOCK` / `@ULOCK` / `@UUNLOCK` - Special lock types
   - `STATS()` - Game statistics
   - Time conversion functions (`CONVSECS()`, `CONVTIME()`, etc.)

3. **Lower Priority**: Specialized or deprecated functions:
   - `SHA0()` - Older hash function (SHA1 is more common)
   - `SOUNDLIKE()` / `SPEAKPENN()` - Speech/phonetic functions
   - `XMWHO()` - XML WHO output (legacy)

