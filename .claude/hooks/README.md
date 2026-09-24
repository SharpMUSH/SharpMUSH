# Claude Code hooks

## `stop-verify.py` (Stop hook)

Registered in `.claude/settings.json`. When Claude Code tries to end a turn in this repository, the hook
refuses (exit 2, with the reason fed back to the agent) while:

1. **A build or test job started in the background is still running.** That covers `dotnet test`,
   `dotnet build`, `dotnet format`, `dotnet run --project *Tests*`, `node --test` and `npm test`
   under the current Claude Code process. Ending the turn would kill the job and lose its result.
   Servers and watchers are not matched, so leaving `SharpMUSH.Server` running is fine.
2. **Changed `.cs` files fail `dotnet format whitespace --verify-no-changes`**, the same check as
   CI's `format` job. The fix command is in the message. Run it until it reports no changes,
   because the formatter needs two passes to converge.
3. **A test project that depends on the changed files fails.** Changed files are mapped to their
   project, then to every `SharpMUSH.Tests*` project that references it directly or indirectly.
   Files a project links in from outside its own directory, such as help files, oracle fixtures
   and embedded package YAML, count for that project too.
   A change to `Directory.Build.*`, `global.json` or `.editorconfig` selects every test project.
   `node --test tools/client-tests/*.test.mjs` runs when `SharpMUSH.Client/wwwroot/` or
   `tools/client-tests/` changes.

"Changed" means the working tree (staged, unstaged, untracked) plus commits not yet on the upstream
branch, or on `origin/main` when there is no upstream.

A pass is remembered in `$(git rev-parse --git-dir)/claude-stop-hook/verified` as a fingerprint of
the changed files' contents, so the tests are not re-run until those files change again. Test and
format logs are kept next to it.

### Cost

- No relevant changes, or unchanged since the last pass: a few git calls, well under a second.
- `.cs` changes: formatting is checked on the changed files only, which takes a couple of seconds.
- Tests: the affected projects, run once for each distinct set of changes.

### Safety valves

- **Loop guard.** After 8 consecutive blocks in one session (`SHARPMUSH_STOP_HOOK_MAX_BLOCKS`), the
  hook lets the turn end and warns that it ended unverified.
- **Tooling problems never block.** If there is no usable .NET SDK for `global.json`, or
  `dotnet format` itself crashes, the hook reports that and lets the turn end. CI is still the gate.
  The hook looks for an SDK on `PATH`, then in `$DOTNET_ROOT` and `~/.dotnet`.
- **Test budget.** The test run stops after 2400s (`SHARPMUSH_STOP_HOOK_TEST_TIMEOUT`) and blocks
  with a message telling the agent to run the tests itself.
- **Off in CI.** The hook does nothing when `CI` or `GITHUB_ACTIONS` is set, for example in the Claude
  GitHub workflows.

### Escape hatch (for humans)

Set these in the environment you launch `claude` from:

```bash
SHARPMUSH_STOP_HOOK=off claude          # disable the hook for this session
SHARPMUSH_STOP_HOOK_TESTS=off claude    # keep the format and background-job checks, skip tests
```

To turn it off for yourself permanently, set `"env": { "SHARPMUSH_STOP_HOOK": "off" }` in your
untracked `.claude/settings.local.json`. `/hooks` in Claude Code shows what is registered.

Agents must not use the escape hatch to get past a failure. Fix the failure instead.
