# Rhost Offline Export Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the implementation and independent review.

**Goal:** Produce a working, tested offline legacy-QDBM/MDBX export route for issue #1963.

**Architecture:** Standalone .NET CLI referencing only SharpMUSH.Library; explicit native source/profile validation, private-copy upstream conversion, strict portable readback, atomic output publication. Existing import pipeline consumes outputs. No runtime world access.

**Tech Stack:** .NET11/C#15, TUnit, upstream Rhost dbconvert and bundled QDBM/MDBX libraries.

**Spec:** ../specs/2026-10-16-rhost-offline-export-design.md

## Global Constraints

- Never mutate the backup or claim unsupported layouts; require explicit matching pinned profile.
- No /tmp: task builds use /temp/codex/rhost-export; tool scratch is private under output parent.
- Keep #1963 open; standalone bridge does not solve live import staging or universal external-attribute completeness.

## Review Focus

- Process pipe faults/cancellation must kill and await child without deadlock or masking original failure.
- Symlinks/path overlap/output races cannot overwrite source or existing output.
- Exporter exit zero cannot substitute for validated complete output and truthful diagnostics.
- Binary fixtures and build provenance must come from real upstream backends, not renamed text samples.
- Source hashes and portable mail validation must be checked before publication.

## Tasks

- [ ] 1. Implement tool contracts, source/profile validation and CLI, with red/green refusal tests.
- [ ] 2. Implement bounded process execution, private-copy export/readback/publication, with real success and deterministic failure/cancellation tests.
- [ ] 3. Commit real native fixtures and reproducible recipe; prove neutral models and imported world equivalence using existing Rhost translator/isolated worlds.
- [ ] 4. Correct migration docs, add exact commands/support matrix/limits and staging follow-up issue.
- [ ] 5. Run affected Rhost/export/import suites, independent whole-branch review, open/attach PR and carry CI/review feedback.
