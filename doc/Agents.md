# Agents

The relay keeps an in-memory registry of agents. An agent is a small Windows service on a client PC.
It holds one gRPC stream open to the relay. A technician picks the PC on the admin site, and the relay tells that agent to
download and start the app. The app then registers under the session GUID it was given, as usual (`/app/{id}`).
The contract is `src/Protos/agent.proto`. Agent and admin-site projects copy it verbatim.

## Experimental

> **The agent registry is an experimental feature.** `AgentHub`, `AgentAdmin`, the `PeakSWC.RemoteWebView.Agents` types and the
> messages generated from `agent.proto` may change or be removed in any release, including minor and patch releases.

Using these types from code produces compiler diagnostic `RWV001`. To opt in, suppress it in the project that uses them:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);RWV001</NoWarn>
</PropertyGroup>
```

or around the use site with `#pragma warning disable RWV001`. Projects that copy `agent.proto` and generate their own
client code don't get the diagnostic, but the contract is just as likely to change.

## Services

| Service | Used by | Transport | Endpoint |
|---|---|---|---|
| `rwvagent.AgentHub` (`AgentHubService`) | the agent | native gRPC (HTTP/2) | `/rwvagent.AgentHub/Connect` |
| `rwvagent.AgentAdmin` (`AgentAdminService`) | the admin site | gRPC-Web, CORS policy `CorsPolicy` | `/rwvagent.AgentAdmin/GetAgents`, `WatchAgents`, `StartSession`, `WatchLaunch`, `CancelLaunch` |

- `AgentHub.Connect` is anonymous at the HTTP level. The first message must be `AgentHello`, otherwise the call ends with `InvalidArgument`.
  An enrollment key that isn't on the allow-list ends it with `PermissionDenied`. A new stream for the same `machine_id` replaces the old one.
- `AgentAdmin` gets the same treatment as `ClientIPC`, the other service the admin site calls: anonymous, gRPC-Web, `CorsPolicy`.
  `GetAgents` and `WatchAgents` are always readable. `StartSession` and `CancelLaunch` are gated by `AGENT_ADMIN_KEY` (below).
- An agent shows as offline 90 s after its last message, or as soon as its stream ends. Offline agents stay listed for 7 days.
- `WatchAgents` sends the full list on every change, and at least every 15 s.
- `WatchLaunch` replays the latest stage first. It ends after READY, DECLINED, FAILED or ENDED. A later ENDED, after READY, is still recorded.
- `StartSession` refuses:
  - an offline computer ("That computer is offline")
  - a computer with a running session ("A session is already running")
  - a computer with a start that hasn't reached READY or a final stage yet ("A start is already in progress for that computer").
    A start that hasn't reported anything for 3 minutes no longer blocks.
- When an agent's stream ends, its starts that hadn't reached READY or a final stage are marked FAILED ("The computer disconnected").
- The decision is `ConsentPolicy.Decide(unattended, ceiling, adminKeyConfigured, adminKeyValid)`:

  | `AGENT_ADMIN_KEY` | `x-admin-key` | Result |
  |---|---|---|
  | not set | (ignored) | allowed, always asks the person at the PC |
  | set | missing or wrong | `PermissionDenied`, "Admin key required" |
  | set | correct | asks, unless the technician asked for unattended **and** the agent was installed with `--consent=unattended` |

Registrations, launches, declines and failures are written to the log and to the in-memory event log. On Linux they appear on the admin "Events" page.

## Environment variables

| Variable | Meaning |
|---|---|
| `AGENT_ENROLLMENT_KEYS` | Comma-separated enrollment keys the relay accepts from agents. If it's unset or empty, any key is accepted and a warning is logged once. |
| `AGENT_ADMIN_KEY` | The admin key. If it's set, `StartSession` and `CancelLaunch` need gRPC metadata (a request header) `x-admin-key` with exactly this value (constant-time comparison), otherwise they fail with `PermissionDenied` "Admin key required". If it's unset, anyone can start a session, but every start asks for consent (no unattended starts) and a warning is logged once. |
| `PUBLIC_BASE_URL` | The relay's public base address, sent to the agent as `server_url` and used for `app_url`. Example: `https://server.remoteblazorwebview.com:34544`. If it's unset, the relay uses the scheme and host of the admin site's `StartSession` request. |

## Keepalive

Kestrel sends HTTP/2 keepalive pings every 30 s on idle connections, and closes a connection after 60 s with no answer (`Program.cs`).
Kestrel doesn't time out a connection while a stream is open, and gRPC turns off the minimum request body rate for streaming calls,
so a quiet `Connect` stream isn't dropped by the server. Agents send a heartbeat every 30 s.
