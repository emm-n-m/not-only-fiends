---
name: run-app
description: Build and launch an isolated NotOnlyFiendsFeed Blazor Server instance, smoke-check its prerendered routes, and stop only that process. Use to verify UI, content-loading, or engine changes in the real app without disturbing an existing instance.
---

# Run and Smoke-Test the Feed App

Launch a test instance on a spare port. Do not kill or replace the user's existing app.

## Workflow

1. Inspect `.env` availability. Without it, state that the app will load public packs only and
   saved-character routes may be unavailable.
2. Check whether the intended test port is free:

   ```bash
   ss -ltnp | rg ':5099'
   ```

   Select another high port if necessary.

3. Build:

   ```bash
   dotnet build NotOnlyFiendsFeed -v q
   ```

4. Launch in a managed PTY/session so the exact process can be stopped later:

   ```bash
   ASPNETCORE_URLS=http://localhost:5099 dotnet run --project NotOnlyFiendsFeed --no-build --no-launch-profile
   ```

5. Poll `/api/health` or `/` until it responds; do not use a fixed long sleep.
6. Smoke-check prerendered routes:
   - `/`
   - `/builder`
   - `/sheet`
   - `/import`
   - `/settings`
   - `/builder/{id}` only when a known test character exists
7. Inspect status codes and response bodies for initialization exceptions, missing-race
   messages, and expected key text.
8. Stop the exact managed process/session. If process discovery is required, resolve only the
   PID listening on the selected test port. Never use `pkill -f dotnet`.

## Limits

`curl` verifies server startup and prerendering, not interactions behind the live Blazor
circuit. State explicitly which interactive paths remain untested; use an available browser
tool when the user's request requires click-level validation.

## Output

Report the URL/port, content-loading mode, routes checked, observed status, interactive limits,
and teardown result.
