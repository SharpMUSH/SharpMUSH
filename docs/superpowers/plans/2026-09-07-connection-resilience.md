# Connection resilience implementation plan

Spec: ../specs/2026-09-07-connection-resilience-design.md

1. Surface existing engine lifecycle contracts and correct readiness/shutdown ordering.
2. Recover consumer failures, use conditional shared-state writes, refresh TTLs and isolate transport failures.
3. Split SocketServer from rendering worker; preserve protocol state and use persistent multiplexed Unix-domain IPC.
4. Consume hashed credentials atomically; persist replay sequence; reconstruct browser sinks; authorize persisted/current binding in the engine without another login.
5. Fence recycled descriptors through registration, disconnect and queued input; revoke resume on auth transitions; refresh long-lived credentials.
6. Add the third image to shared publishing/PR impact analysis and deployment wiring.
7. Validate socket/worker replacement, browser restoration and rejection/races; measure IPC overhead; review, commit, push and open PR with migration impact.
