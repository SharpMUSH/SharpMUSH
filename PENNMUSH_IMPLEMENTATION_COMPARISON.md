# PennMUSH Implementation Comparison

**Generated:** 2026-01-05

This document tracks the implementation status of PennMUSH commands and functions in SharpMUSH.

**Analysis Method:** Extracted directly from PennMUSH source code using regex parsing of FUNCTION() and COMLIST declarations. Command switches and metadata flags have been filtered out.

## Summary

| Category | PennMUSH | SharpMUSH | Missing | Coverage |
|----------|----------|-----------|---------|----------|
| **Commands** | 170 | 171 | 4 | 97% |
| **Functions** | 407 | 526 | 21 | 94% |

## Missing Commands

The following PennMUSH commands are not yet implemented in SharpMUSH:

- [ ] `@ELOCK`
- [ ] `@EUNLOCK`
- [ ] `@ULOCK`
- [ ] `@UUNLOCK`

## Missing Functions

The following PennMUSH functions are not yet implemented in SharpMUSH:

- [ ] `ANSIGEN()`
- [ ] `CINFO()`
- [ ] `CONFIG()`
- [ ] `CONVSECS()`
- [ ] `CONVTIME()`
- [ ] `DBWALKER()`
- [ ] `DELETE()`
- [ ] `HOSTNAME()`
- [ ] `IDLESECS()`
- [ ] `INSERT()`
- [ ] `LCSTR2()`
- [ ] `PE_REGS_DUMP()`
- [ ] `REGREPLACE()`
- [ ] `RNUM()`
- [ ] `SETMANIP()`
- [ ] `SHA0()`
- [ ] `SOUNDLIKE()`
- [ ] `STR_REP_OR_INS()`
- [ ] `UCSTR2()`
- [ ] `WEBSOCKET_HTML()`
- [ ] `WEBSOCKET_JSON()`

## SharpMUSH Extensions - Commands

These commands are implemented in SharpMUSH but not found in PennMUSH:

*Note: Some of these may be SharpMUSH-specific implementations or connection-level commands.*

- `&`
- `@MAP`
- `CONNECT`
- `QUIT`
- `]`

## SharpMUSH Extensions - Functions

These functions are implemented in SharpMUSH but not found in PennMUSH source:

*Note: Some may be SharpMUSH-specific enhancements or equivalent functions with different names.*

- `@@()`
- `ANDLPOWERS()`
- `ATTRIB_SET#()`
- `CASE()`
- `CASEALL()`
- `CBUFFER()`
- `CDESC()`
- `CHILDREN()`
- `CLFLAGS()`
- `CMSGS()`
- `CNAND()`
- `COND()`
- `CONDALL()`
- `CUSERS()`
- `DECOMPOSEWEB()`
- `DOWNMOTD()`
- `ELIST()`
- `FILTERBOOL()`
- `FULLMOTD()`
- `GETPIDS()`
- `GREPI()`
- `HASATTRP()`
- `HASATTRPVAL()`
- `HASATTRVAL()`
- `HOST()`
- `IDLE()`
- `IFELSE()`
- `LALIGN()`
- `LATTRP()`
- `LCON()`
- `LEXITS()`
- `LINSERT()`
- `LLOCKFLAGS()`
- `LLOCKS()`
- `LPLAYERS()`
- `LREPLACE()`
- `LSEARCHR()`
- `LTHINGS()`
- `LVCON()`
- `LVEXITS()`
- `LVPLAYERS()`
- `LVTHINGS()`
- `LWHOID()`
- `MAILDSTATS()`
- `MAILFSTATS()`
- `MOTD()`
- `MWHO()`
- `MWHOID()`
- `NATTRP()`
- `NCHILDREN()`
- `NCON()`
- `NCOND()`
- `NCONDALL()`
- `NCOR()`
- `NEXITS()`
- `NLSEARCH()`
- `NMWHO()`
- `NPLAYERS()`
- `NSCEMIT()`
- `NSEARCH()`
- `NSEMIT()`
- `NSLEMIT()`
- `NSOEMIT()`
- `NSPEMIT()`
- `NSPROMPT()`
- `NSREMIT()`
- `NSZEMIT()`
- `NTHINGS()`
- `NVCON()`
- `NVEXITS()`
- `NVPLAYERS()`
- `NVTHINGS()`
- `ORDINAL()`
- `ORLPOWERS()`
- `PGREP()`
- `RANDEXTRACT()`
- `REGEDIT()`
- `REGEDITALL()`
- `REGEDITALLI()`
- `REGEDITI()`
- `REGISTERS()`
- `REGLATTR()`
- `REGLATTRP()`
- `REGLMATCH()`
- `REGLMATCHALL()`
- `REGLMATCHI()`
- `REGMATCHALLI()`
- `REGMATCHI()`
- `REGNATTR()`
- `REGNATTRP()`
- `REGRABALL()`
- `REGRABALLI()`
- `REGRABI()`
- `REGREP()`
- `REGREPI()`
- `REGXATTR()`
- `REGXATTRP()`
- `RENDERMARKDOWN()`
- `RENDERMARKDOWNCUSTOM()`
- `RESWITCHALL()`
- `RESWITCHALLI()`
- `RESWITCHI()`
- `SETDIFF()`
- `SETINTER()`
- `SETR()`
- `SETSYMDIFF()`
- `SETUNION()`
- `SOUNDSLIKE()`
- `SQLESCAPE()`
- `STRALLOF()`
- `STRDELETE()`
- `STRFIRSTOF()`
- `STRINSERT()`
- `STRREPLACE()`
- `SWITCHALL()`
- `TRIMPENN()`
- `TRIMTINY()`
- `ULAMBDA()`
- `ULDEFAULT()`
- `ULOCAL()`
- `UTCTIME()`
- `VDIM()`
- `WILDGREP()`
- `WILDGREPI()`
- `WIZMOTD()`
- `WSHTML()`
- `WSJSON()`
- `XATTR()`
- `XATTRP()`
- `XCON()`
- `XEXITS()`
- `XMWHOID()`
- `XPLAYERS()`
- `XTHINGS()`
- `XVCON()`
- `XVEXITS()`
- `XVPLAYERS()`
- `XVTHINGS()`
- `XWHOID()`
- `ZFIND()`
- `ZMWHO()`

## Notes

### Methodology
- **Source**: Direct extraction from PennMUSH C source files (all .c files in src/)
- **Commands**: Parsed from COMLIST commands[] in command.c
- **Functions**: Extracted FUNCTION() declarations from all source files
- **Filtering**: Removed command switches/flags (EQSPLIT, RS_ARGS, etc.) and object types (ROOM, THING, etc.)

### Key Findings
- **Channel Functions**: Both implementations include channel functions (CWHO, CSTATUS, CEMIT, etc.)
- **Mail Functions**: Both implementations include mail functions (MAIL, MAILFROM, MAILSTATS, etc.)
- **Player Commands**: Commands like SCORE exist in both systems
- **Connection Commands**: QUIT and CONNECT may be handled at different levels in each system
- **Naming Variations**: Some functions have different names (e.g., PennMUSH's SQL_ESCAPE is implemented as SQLESCAPE in SharpMUSH)

## Priority Recommendations

Based on missing items that affect MUSH code compatibility:

### High Priority Functions
Essential for running existing MUSH softcode:

- `CONFIG()`
- `DELETE()`
- `HOSTNAME()`
- `IDLESECS()`
- `INSERT()`

### Medium Priority
Useful utility functions:

- `CONVSECS()`
- `CONVTIME()`
- `LCSTR2()`
- `RNUM()`
- `SHA0()`
- `UCSTR2()`

### Lock Commands
- `@ELOCK`
- `@EUNLOCK`
- `@ULOCK`
- `@UUNLOCK`

### Implementation Notes
- Some "missing" items may already be implemented with different names
- SharpMUSH's additional functions extend functionality beyond PennMUSH
- Regular updates recommended as both codebases evolve
