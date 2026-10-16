# Remote Backup Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Manage remote Restic backups from the administrator website.

**Architecture:** Server-owned persisted jobs and configuration; isolated fixed-command worker claims and acknowledges jobs. Safe provider hot copies precede remote upload.

**Tech Stack:** .NET 11, Blazor, existing secret store, Python stdlib, Restic JSON.

**Spec:** docs/superpowers/specs/2026-10-16-remote-backup-control-design.md

## Global Constraints

No live LMDB snapshots, Docker socket, shell command construction, plaintext ordinary reads, automatic prune or restore. Unknown worker completion retains its copy. Configuration changes affect future jobs only.

## Review Focus

- Invalid repository strings cannot inject commands or disclose credentials.
- Restart during preparation or upload cannot fabricate success.
- Replayed or mismatched receipts cannot finish another job.
- Partial Restic exit three remains a failure even with a snapshot identifier.
- Local retention cannot delete a job's independent completed copy.

### Task 1: Durable coordinator and provider copy

- [x] Write coordinator validation, receipt and restart tests; confirm red.
- [x] Add DTOs, job state and scoped worker contract, protected immutable configuration and provider-safe job copy.
- [x] Run focused tests and formatting.

### Task 2: Isolated Restic worker

- [x] Write fixed argv, partial-exit, output and receipt retry tests; confirm red.
- [x] Implement authenticated polling with persistent pending receipt, timeout, fixed sources and snapshot reconciliation.
- [x] Run Python unit tests and a local temporary repository integration if Restic is available.

### Task 3: Website and deployment

- [x] Add permission-protected admin API and website controls, masked secrets, bounded history and agent health.
- [x] Document legacy migration and repository initialization, add opt-in worker compose image and deployment token.
- [x] Build server and client; run coordinator, component, worker and real local repository tests.
- [ ] Open draft PR; complete independent whole-branch security review before marking ready.
