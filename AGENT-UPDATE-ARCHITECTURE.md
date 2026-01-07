# Agent Auto-Update System Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Azure Blob Storage                          │
│                                                                     │
│  ┌──────────────────┐         ┌────────────────────────────────┐  │
│  │  appcast.json    │         │  Update ZIP Files              │  │
│  │  ├─ Version      │         │  ├─ ProdControlAV-Agent-      │  │
│  │  ├─ Download URL │────────▶│  │   1.0.0-linux-arm64.zip    │  │
│  │  ├─ Ed25519 Sig  │         │  ├─ ProdControlAV-Agent-      │  │
│  │  └─ Metadata     │         │  │   1.0.1-linux-arm64.zip    │  │
│  └──────────────────┘         │  └─ ...                        │  │
│         ▲                      └────────────────────────────────┘  │
└─────────┼────────────────────────────────▲───────────────────────┘
          │                                │
          │ 1. Check hourly               │ 2. Download on update
          │    for updates                │    detected
          │                                │
┌─────────┴────────────────────────────────┴───────────────────────┐
│                    Raspberry Pi - Agent                           │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  UpdateService (Background Service)                      │    │
│  │  ├─ Check appcast.json every hour (3600s)              │    │
│  │  ├─ Detect manual trigger via signal file              │    │
│  │  ├─ Download update ZIP if available                   │    │
│  │  ├─ Create backup: /opt/prodcontrolav/agent.TIMESTAMP  │    │
│  │  ├─ Extract and install update                         │    │
│  │  ├─ Rollback on failure                                │    │
│  │  └─ Exit(0) - systemd restarts agent                   │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  CommandService                                          │    │
│  │  ├─ Poll command queue every 10s                       │    │
│  │  ├─ Receive UPDATE command                             │    │
│  │  └─ Create signal file: /tmp/prodcontrolav-update-trigger    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  AgentService                                            │    │
│  │  └─ Monitors devices, sends heartbeats                  │    │
│  └─────────────────────────────────────────────────────────┘    │
│         │                                                         │
└─────────┼─────────────────────────────────────────────────────┘
          │ 3. Heartbeat with version
          │    every 60s
          ▼
┌─────────────────────────────────────────────────────────────────┐
│                    API Server (Azure)                            │
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  AgentHealthController                                  │    │
│  │  ├─ GET /api/agent/health-dashboard                    │    │
│  │  │   └─ Returns agent status with version info         │    │
│  │  │                                                      │    │
│  │  └─ POST /api/agent/{id}/trigger-update                │    │
│  │      ├─ DevAdmin authorization required                │    │
│  │      ├─ Validates agent in tenant                      │    │
│  │      └─ Creates UPDATE command in queue                │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  Azure Table Storage                                    │    │
│  │  ├─ AgentAuth (versions, last seen)                    │    │
│  │  ├─ CommandQueue (pending commands)                    │    │
│  │  └─ CommandHistory (execution results)                 │    │
│  └────────────────────────────────────────────────────────┘    │
│         │                                                        │
└─────────┼────────────────────────────────────────────────────┘
          │ 4. Fetch dashboard data
          │    and trigger updates
          ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Web UI (Blazor WASM)                          │
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐    │
│  │  Agent Health Dashboard                                 │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │  Agent: RaspberryPi-01                           │  │    │
│  │  │  Status: ● Online                                │  │    │
│  │  │  Version: 1.0.0 ⚠️ 1.0.1  [Apply Update]       │  │    │
│  │  │  Last Seen: 2m ago                               │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  │                                                          │    │
│  │  User clicks [Apply Update]                             │    │
│  │  ↓                                                       │    │
│  │  ┌──────────────────────────────────────────────────┐  │    │
│  │  │  ⚠️ Confirm Agent Update                         │  │    │
│  │  │                                                   │  │    │
│  │  │  Update RaspberryPi-01?                          │  │    │
│  │  │                                                   │  │    │
│  │  │  Current: 1.0.0 → New: 1.0.1                     │  │    │
│  │  │                                                   │  │    │
│  │  │  • Backup will be created                        │  │    │
│  │  │  • Agent will restart                            │  │    │
│  │  │  • Takes ~2 minutes                              │  │    │
│  │  │                                                   │  │    │
│  │  │  [Cancel]  [Apply Update]                        │  │    │
│  │  └──────────────────────────────────────────────────┘  │    │
│  └────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

## Update Flow - Automatic Mode

```
┌─────────────────────────────────────────────────────────────────┐
│  T+0:00   New Release Published                                 │
│           └─ GitHub Actions builds and signs                    │
│           └─ Uploads ZIP to Azure Blob Storage                  │
│           └─ Updates appcast.json with new version              │
├─────────────────────────────────────────────────────────────────┤
│  T+0:00 - T+1:00   Waiting for next update check               │
│                    (CheckIntervalSeconds: 3600)                 │
├─────────────────────────────────────────────────────────────────┤
│  T+1:00   UpdateService checks for updates                      │
│           └─ Downloads appcast.json                             │
│           └─ Compares 1.0.1 (available) vs 1.0.0 (current)     │
│           └─ Update detected!                                   │
├─────────────────────────────────────────────────────────────────┤
│  T+1:00   Backup Creation                                       │
│           └─ Creates /opt/prodcontrolav/agent.2026-01-07_14-00 │
│           └─ Copies all agent files recursively                 │
│           └─ Logs backup location                               │
├─────────────────────────────────────────────────────────────────┤
│  T+1:01   Download Update                                       │
│           └─ Downloads from Azure Blob Storage                  │
│           └─ Saves to /tmp/prodcontrolav-update-1.0.1.zip      │
│           └─ Verifies Ed25519 signature                         │
├─────────────────────────────────────────────────────────────────┤
│  T+1:02   Install Update                                        │
│           └─ Extracts ZIP to temp directory                     │
│           └─ Validates extraction                               │
│           └─ Copies files to /opt/prodcontrolav/agent/         │
│           └─ Overwrites old DLLs, configs, etc.                │
├─────────────────────────────────────────────────────────────────┤
│  T+1:02   Restart Agent                                         │
│           └─ Logs success and backup location                   │
│           └─ Waits 5 seconds for log flush                      │
│           └─ Calls Environment.Exit(0)                          │
│           └─ Systemd detects exit                               │
│           └─ Systemd restarts agent (Restart=always)           │
├─────────────────────────────────────────────────────────────────┤
│  T+1:03   New Version Running                                   │
│           └─ Agent starts with new binaries                     │
│           └─ Logs: "Current agent version: 1.0.1"              │
│           └─ Sends heartbeat with new version                   │
│           └─ Health dashboard shows 1.0.1 (up to date)         │
└─────────────────────────────────────────────────────────────────┘
```

## Update Flow - Manual Trigger

```
┌─────────────────────────────────────────────────────────────────┐
│  T+0:00   User Views Health Dashboard                           │
│           └─ Sees agent with orange badge: ⚠️ 1.0.1            │
│           └─ Clicks "Apply Update" button                       │
├─────────────────────────────────────────────────────────────────┤
│  T+0:01   Confirmation Dialog                                   │
│           └─ Shows: "Update RaspberryPi-01?"                    │
│           └─ Shows: "Current: 1.0.0 → New: 1.0.1"              │
│           └─ Shows warnings about restart                       │
│           └─ User clicks "Apply Update"                         │
├─────────────────────────────────────────────────────────────────┤
│  T+0:02   API Processes Request                                 │
│           └─ POST /api/agent/{id}/trigger-update               │
│           └─ Validates user has DevAdmin role                   │
│           └─ Validates agent belongs to tenant                  │
│           └─ Creates UPDATE command in CommandQueue            │
│           └─ Returns 200 OK with command ID                     │
├─────────────────────────────────────────────────────────────────┤
│  T+0:02   UI Shows Progress                                     │
│           └─ Button shows: "🔄 Applying..."                     │
│           └─ Spinner animation                                  │
├─────────────────────────────────────────────────────────────────┤
│  T+0:10   Agent Polls Command Queue                             │
│           └─ CommandService checks every 10 seconds            │
│           └─ Receives UPDATE command                            │
│           └─ Creates signal file at /tmp/prodcontrolav-update-trigger │
│           └─ Marks command as processing                        │
├─────────────────────────────────────────────────────────────────┤
│  T+0:15   UpdateService Detects Signal                          │
│           └─ Sees signal file on next loop iteration           │
│           └─ Deletes signal file                                │
│           └─ Immediately checks for updates (bypasses 1hr wait)│
│           └─ Follows same backup → download → install flow     │
├─────────────────────────────────────────────────────────────────┤
│  T+2:00   Update Complete                                       │
│           └─ Agent restarted with new version                   │
│           └─ UI shows: "✓ Update successfully applied"         │
│           └─ Dashboard auto-refreshes                           │
│           └─ Agent now shows version 1.0.1 (up to date)        │
└─────────────────────────────────────────────────────────────────┘
```

## Rollback Flow

```
┌─────────────────────────────────────────────────────────────────┐
│  Backup Phase                                                    │
│  └─ ✓ Backup created at /opt/prodcontrolav/agent.2026-01-07... │
├─────────────────────────────────────────────────────────────────┤
│  Download Phase                                                  │
│  └─ ✓ ZIP downloaded successfully                               │
├─────────────────────────────────────────────────────────────────┤
│  Install Phase                                                   │
│  └─ ✗ ERROR: File copy failed (disk full)                      │
├─────────────────────────────────────────────────────────────────┤
│  Rollback Triggered                                              │
│  └─ Logs: "Update failed. Attempting rollback from backup"     │
│  └─ Deletes partial installation files                          │
│  └─ Copies files from backup directory                          │
│  └─ Restores complete previous version                          │
│  └─ Logs: "Rollback completed successfully"                    │
├─────────────────────────────────────────────────────────────────┤
│  Service Continues                                               │
│  └─ Agent continues running on version 1.0.0                   │
│  └─ Error logged for investigation                              │
│  └─ Backup preserved for manual inspection                      │
│  └─ Next update check happens normally in 1 hour               │
└─────────────────────────────────────────────────────────────────┘
```

## File System Structure

```
/opt/prodcontrolav/
├── agent/                          ← Active agent installation
│   ├── ProdControlAV.Agent         ← Main executable
│   ├── ProdControlAV.Agent.dll
│   ├── appsettings.json
│   ├── NetSparkleUpdater.dll
│   └── ... (other dependencies)
│
├── agent.2026-01-07_13-45-12/     ← Backup from 13:45 update
│   ├── ProdControlAV.Agent
│   └── ... (complete copy)
│
├── agent.2026-01-07_14-30-15/     ← Backup from 14:30 update
│   ├── ProdControlAV.Agent
│   └── ... (complete copy)
│
└── agent.2026-01-07_15-00-22/     ← Most recent backup (15:00)
    ├── ProdControlAV.Agent
    └── ... (complete copy)

/tmp/
├── prodcontrolav-update-trigger    ← Signal file (exists briefly)
├── prodcontrolav-update-1.0.1.zip  ← Downloaded update (cleaned after install)
└── prodcontrolav-extract-1.0.1/    ← Temp extraction (cleaned after install)
    ├── ProdControlAV.Agent
    └── ... (extracted contents)
```

## Security Layers

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer 1: Signature Verification                                │
│  └─ NetSparkle validates Ed25519 signature                      │
│  └─ SecurityMode.Strict enforced                                │
│  └─ Invalid signature = update rejected                         │
├─────────────────────────────────────────────────────────────────┤
│  Layer 2: HTTPS Transport                                        │
│  └─ All downloads over HTTPS from Azure                         │
│  └─ Certificate validation                                       │
│  └─ No insecure HTTP connections                                │
├─────────────────────────────────────────────────────────────────┤
│  Layer 3: Authorization                                          │
│  └─ Manual triggers require DevAdmin role                       │
│  └─ Tenant isolation enforced                                   │
│  └─ JWT authentication for API calls                            │
├─────────────────────────────────────────────────────────────────┤
│  Layer 4: Backup & Rollback                                      │
│  └─ Backup created before any file changes                      │
│  └─ Automatic rollback on installation failure                  │
│  └─ Previous version always preserved                           │
├─────────────────────────────────────────────────────────────────┤
│  Layer 5: Logging & Audit                                        │
│  └─ All update actions logged                                   │
│  └─ Backup locations recorded                                   │
│  └─ Errors captured for investigation                           │
│  └─ Manual triggers logged with user ID                         │
└─────────────────────────────────────────────────────────────────┘
```

## State Diagram

```
┌───────────────┐
│   Checking    │ ←─────────────────┐
│  for Updates  │                   │
└───────┬───────┘                   │
        │                           │
        ├─ No Update Available      │
        │  └─ Wait 1 hour ──────────┘
        │
        └─ Update Available
           │
           ▼
┌───────────────┐
│   Creating    │
│    Backup     │
└───────┬───────┘
        │
        ├─ Backup Failed ─────────┐
        │                         │
        └─ Backup Success         │
           │                      │
           ▼                      │
┌───────────────┐                │
│  Downloading  │                │
│    Update     │                │
└───────┬───────┘                │
        │                        │
        ├─ Download Failed ──────┤
        │                        │
        └─ Download Success      │
           │                     │
           ▼                     │
┌───────────────┐               │
│  Installing   │               │
│    Update     │               │
└───────┬───────┘               │
        │                       │
        ├─ Install Failed ──────┤
        │                       │
        └─ Install Success      │
           │                    │
           ▼                    │
┌───────────────┐               │
│  Restarting   │               │
│     Agent     │               │
└───────┬───────┘               │
        │                       │
        ▼                       │
┌───────────────┐               │
│  New Version  │               │
│    Running    │               │
└───────────────┘               │
                                │
                                ▼
                        ┌───────────────┐
                        │   Rollback    │
                        │   to Backup   │
                        └───────┬───────┘
                                │
                                ▼
                        ┌───────────────┐
                        │ Old Version   │
                        │   Running     │
                        └───────────────┘
```
