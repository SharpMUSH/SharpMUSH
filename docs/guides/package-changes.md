# Reviewing package changes

Package changes use the ordinary installation and uninstall lifecycle. Installing a package applies its managed content and records an installed revision. A first install queues its `AINSTALL` hooks; an upgrade uses `AUPDATE`. Uninstall removes the effects managed by the ordinary uninstall operation. Objects the package created are halted and marked `GOING` for garbage collection; they need not disappear from the database immediately. It is not a way to retain installed content while marking a package inactive.

Add the packages and plugin state changes you want to the change queue, then review the server's proposed order. Dependencies install before the packages that require them. When removing both a dependent package and its prerequisite, the dependent uninstalls first.

Suggested prerequisites are proposals. Add them explicitly and review again before applying. The server does not silently add dependencies, remove dependents, choose conflict resolutions, or approve plugin code. An incompatible dependency or an unselected dependent can block the batch. Resolve the listed blocker and request a new review.

Each install retains its ordinary configuration, conflict and code trust review. Each uninstall requires confirmation. A review token refers to the reviewed sources, installed state and choices; changes to that state require a fresh review. Removing an item from the queue changes what is requested, not what is already installed.

At the first execution check, the server refreshes the sources and checks that they still match the accepted review. The admitted run then uses that exact payload; later remote commits do not change its files. Permissions, configured source trust, content conflicts and plugin requirements are still checked during execution. A batch resolves at most 32 sources and retains at most 64 MiB of counted text and binary payloads; this is a payload limit, not a bound on total process memory.

One automatic pre-package backup precedes the batch when backups are enabled. The batch is not an atomic transaction and does not automatically roll back. If a later step fails, earlier completed changes remain installed or removed, including their lifecycle effects. Inspect actual package state, fix the reported problem and review a new request for the remaining work.

Plugin registration, enabled state and running state are separate. Enabling or disabling a plugin can require a server restart. A restart boundary does not claim that code was hot-loaded or unloaded. Restart when instructed, refresh actual state and create a fresh review for dependent work.

Run progress belongs to the server process. If progress is lost, expired or unknown, refresh installed packages and plugin state before retrying. An unknown result does not mean that no changes happened. Do not repeat an old plan on the assumption that completed steps were undone.
