# Table Storage Device Sync Requirements (revised)

**Path**: `docs/requirements/table-storage-device-sync.md`  
**Purpose**: Make Azure Table Storage the canonical source for dashboard device listings and status, and ensure SQL → Table Storage synchronization for all device writes while maintaining SaaS multi-tenancy, security, and low cost.

## Summary (decisions)
- Dashboard reads: Table Storage only (cheap, fast point reads/scan per tenant).
- Device status: persisted and read from Table Storage.
- Authoritative relational history remains in Azure SQL; Table Storage is the read-optimized materialized view used by UI.
- Replication architecture (recommended): SQL write → Outbox row → Azure Queue (or Service Bus Basic/Standard if ordering/visibility needed) → Worker/Function → Table Storage upsert.
  - Outbox ensures durability / transactional coupling with SQL and avoids lost replication on partial failures.
- Use Azure Table Storage (Storage Account Tables) for cost efficiency, not Cosmos DB Table API, unless cross-region read / global distribution is required.

## Multi-tenancy & Partitioning
- PartitionKey = tenantId.ToString().ToLowerInvariant()
- RowKey = deviceId.ToString()
- Table name convention: `devices-v1` (or tenant-agnostic single-table with PartitionKey isolation).
- Enforce tenant isolation at every layer: API, background workers, reconciliation and migration tooling must validate tenantId in the token/outbox row before writing to Table Storage.

## Security
- Follow the repository's established security architecture rather than introducing a separate, ad-hoc model for Table Storage.
  - Canonical locations:
    - API authentication, tenant isolation and related code: `src/ProdControlAV.API/Auth/`.
    - Environment configuration and connection strings: `src/ProdControlAV.API/appsettings.json`.
    - Operational infra/runbooks and role assignment guidance: consult the team's infra repo / runbooks (ops documentation).
- Principles to apply (enforced via central config/code and infra):
  - Prefer Managed Identity (TokenCredential) for production Storage access; do not persist Storage account primary keys or long-lived secrets in `appsettings`.
  - For run-once migration tools, use short-lived SAS tokens or temporary elevated identities per the standard runbook and audit those operations.
  - Enforce least-privilege role assignments and tenant isolation at both the infra and application layers (PartitionKey + authorization checks).
  - Network controls (private endpoint / firewall) and diagnostics (Log Analytics / App Insights) must be configured by central infra templates; workers/apps should use the configured endpoints and send diagnostics to the central monitoring workspace.
- If gaps are found when implementing Table Storage access, update the central security docs or the infra runbook and reference that change here instead of duplicating policies.

## Data Model (Table entity)
- Required fields to store in table entity:
  - PartitionKey (tenantId)
  - RowKey (deviceId)
  - Name (string)
  - IpAddress (string)
  - Type (string)
  - Model (string)
  - Brand (string)
  - Location (string)
  - Port (int)
  - AllowTelNet (bool)
  - CreatedUtc (DateTimeOffset)
  - Status (string) — e.g., "Online", "Offline", "Unknown"
  - LastSeenUtc (DateTimeOffset?)
  - LastPolledUtc (DateTimeOffset?)
  - Optional: IsOnline (bool), HealthMetric (double)
  - Concurrency token: optional ETag usage for optimistic concurrency on full replace flows
- Size/cost considerations: avoid storing large blobs in Table rows. Keep fields compact and typed.

## Table update modes and patterns
- Full entity upserts: TableUpdateMode.Replace (replace whole entity) — used rarely for metadata updates.
- Status-only updates: TableUpdateMode.Merge (merge only status-related fields) — MUST be used for high-frequency status updates to minimize transaction size and cost.
- Batching: group multiple upserts/merges into TableBatchOperation where partition allows (same PartitionKey) to reduce transaction count and cost.
- Idempotency: all worker writes must be idempotent — deduplicate by outbox id or include a lastUpdatedUtc compare to avoid re-ordering issues.

## Writes / Consistency & Replication mechanism
- PRINCIPLE: Device status MUST NOT be written to SQL. Current device status is authoritative in Table Storage only.
- Authoritative responsibilities:
  - SQL (Azure SQL) stores device metadata and relational history (owner, createdBy, long-term audit records), but it MUST NOT contain frequently-updated status columns (IsOnline/Status/LastSeenUtc etc.).
  - Table Storage stores the current/latest device status (PartitionKey=tenantId, RowKey=deviceId) and is the source-of-truth for dashboard reads.
- Status write flow (required):
  1. All status updates MUST be published to the queue (Azure Queue Storage or Service Bus) by agents or by server-side code that needs to change status. Message payload: { tenantId, deviceId, status, lastSeenUtc, lastPolledUtc, lastUpdatedUtc, idempotencyKey, agentId?, traceId? }.
  2. Worker/Function consumes the queue message and performs an idempotent Table Storage merge upsert of only the status-related fields (Status, LastSeenUtc, LastPolledUtc, optional IsOnline/HealthMetric). The worker must validate tenantId and monotonic LastUpdatedUtc before applying the merge to avoid regressions.
  3. On Table write success: emit metrics/logs and acknowledge the queue message.
  4. On Table write failure: retry with exponential backoff; after N attempts move to DLQ and emit alerts/metrics (tenantId, deviceId, idempotencyKey).
- SQL-originated metadata changes (Device updates):
  - If a change to device metadata requires reflecting into Table Storage (for example, Name, Location), the SQL transaction that updates Device metadata MUST write an Outbox entry in the same transaction describing the metadata change for the Table writer to apply (full replace or selective fields). The outbox is for metadata replication only, not for status.
  - Do NOT place status fields into the Outbox payload as a mechanism to keep status in SQL — status must remain queue→Table.
- Idempotency & ordering:
  - All status messages must include an idempotency key or lastUpdatedUtc to ensure idempotent processing and last-write-wins semantics in the Table writer.
  - Worker should apply monotonic timestamp checks and drop stale messages (lastUpdatedUtc older than stored LastUpdatedUtc) to avoid regressions.
- Reconciliation & failure handling:
  - Reconciliation jobs may compare SQL metadata vs Table status for parity on metadata fields only; status reconciliation compares upstream authoritative sources (agents/telemetry) vs Table entries and enqueues missing/stale items for reprocessing.
  - Any server-side code that attempted to write status into SQL must be updated to publish to the queue instead; during migration/cutover run reconciliation to detect stray status columns and move their values into Table Storage one-time via `SqlToTableBackfill` (for initial backfill only) and then drop those columns.

## Cost optimizations
- Use Storage Tables (cheaper per transaction) and:
  - Merge-only for status updates to reduce write size.
  - Batch writes by tenant PartitionKey where possible.
  - Limit reconciliation frequency; use incremental scans (change detection) rather than full scans frequently.
  - Sample metrics in high cardinality scenarios; aggregate where possible.
  - Use Azure Monitor metric alerts with conservative thresholds to avoid noisy paging.
- Use Basic tier Service Bus or Azure Queue Storage depending on ordering/visibility requirements. Prefer Azure Queue Storage for lowest cost when FIFO isn't required.
- Consider low-cost caching on the API (short in-memory cache, invalidated by events) to avoid repeated Table reads during page navigation.

## Migration / Backfill
- If your Table Storage already contains the device rows (as in test or staging environments), a full backfill is OPTIONAL and can be skipped.
- Recommended approach when Table Storage already has device entries:
  - Verify counts and a small sample of device rows match SQL device metadata (names, ids, tenantId).
  - If status columns are already populated in Table Storage and are current, SKIP any backfill of status.
  - If Table Storage contains devices but no status (or you want to seed initial status), run the backfill tool in a limited/dry-run mode for only the missing pieces.
- Tool (optional): `SqlToTableBackfill` (idempotent, resumable, parallelizable) — only required when Table Storage is not already seeded with devices/status.
  - Reads devices + latest status from SQL and writes to Table Storage using batching per tenant partition.
  - Supports dry-run verification and sample verification mode for very large tenants.
  - Must support resume: store progress markers (tenantId + maxDeviceId processed or a row checkpoint).
- Verification:
  - Post-copy (if performed), run parity checks:
    - Counts per tenant.
    - Sample field equality (e.g., 1% sample or seeded sample keys).
    - Hash/Sum of selected fields per tenant (if size allows).
  - Report mismatches and optionally re-run failed items into a retry queue.

## Error handling & Monitoring
- Logs: include tenantId, deviceId, outboxId, workerId, and error details. Send to Application Insights / Log Analytics.
- Metrics:
  - Sync successes/failures per tenant.
  - Retry queue length / DLQ length.
  - Table write latency and throttling events.
  - Reconciliation items enqueued.
- Alerts:
  - DLQ > threshold
  - Retry queue growth rate sustained
  - Per-tenant failure spike
- Observability: correlate logs across SQL write → outbox → queue → worker → table for troubleshooting.

## Tests & Validation
- Unit tests:
  - `TableDeviceStore.UpsertStatusAsync` uses `TableUpdateMode.Merge` and merges only the status fields.
  - `GetAllForTenantAsync` maps status fields into `DeviceDto`.
  - Outbox creation logic is invoked in the same SQL transaction when Device/DeviceStatus is updated.

## Acceptance Criteria (explicit & testable)
- Dashboard lists devices + latest status using Table Storage only (no direct SQL reads for dashboard endpoints).
- Updating `Device` triggers an Outbox write in same SQL transaction.
- Updating `DeviceStatus` should only update status fields in Table Storage for the specified device.
- Migration utility copies all existing devices and statuses; verification confirms parity (counts + sample field checks).
- Security: Storage access is via Managed Identity and private endpoint or firewall (no secrets in appsettings). Audit logs are available.
- Cost: Table Storage chosen and merge/batching strategy reduces write transactions compared to replace-only approach (verify with sample cost model).
- Automated tests for `TableDeviceStore`, outbox behavior, and migration pass.

## Migration & cutover plan
1. Verify Table Storage state (instead of blind backfill):
   - If Table Storage already contains the expected device rows and statuses for your test/staging environment, you may SKIP a full backfill.
   - Run quick parity checks (counts + sample rows) to confirm.
2. Start dual-path writes (short window, e.g., 24–72h) — optional in test mode:
   - Agents/servers publish status events to the queue. In testing environments where no production data exists, you may start with queue-only writes immediately.
   - Worker must write current status (Merge) into Table Storage.
   - Run reconciliation to verify parity between SQL (metadata) and Table current-status during the window.
3. Cutover:
   - Once Table Storage reads and dashboard flows are verified, stop any legacy status writes to SQL (if any remain) and keep only metadata in SQL.
   - Verify dashboards and audit retrievals work from Table Storage.
4. Cleanup:
   - Remove status columns from SQL schema (drop columns) after successful verification and a safety period, if applicable.
   - Ensure Outbox remains for SQL-originating flows requiring atomicity.
5. Verification:
   - Validate counts, sample events, and retention enforcement (ensure records older than 8 days are deleted).

## Implementation Checklist (developer tasks)
- ProdControlAV.Core
  - Update `DeviceDto` to include status fields: Status, LastSeenUtc, LastPolledUtc, HealthMetric.
- ProdControlAV.Infrastructure
  - Implement `ITableDeviceStore` with methods:
    - `Task UpsertAsync(Guid tenantId, Guid deviceId, DeviceDto dto, CancellationToken ct)`
    - `Task UpsertStatusAsync(Guid tenantId, Guid deviceId, string status, DateTimeOffset lastSeenUtc, DateTimeOffset lastPolledUtc, CancellationToken ct)` — must use TableUpdateMode.Merge.
    - `IAsyncEnumerable<DeviceDto> GetAllForTenantAsync(Guid tenantId, CancellationToken ct)`
  - Implement TableDeviceStore to:
    - Use Managed Identity for auth (TokenCredential).
    - Use batching per tenant.
    - Respect Merge vs Replace usage.
    - Provide robust logging and metrics.
  - Implement retention enforcement (no archive):
    - Implement a scheduled cleanup job (e.g., Azure Function Timer / WebJob) that deletes Table rows where LastSeenUtc < now - 8 days in batch operations.
    - Keep progress/checkpoints to allow resumable scans for very large tenants.
    - Expose metrics for deletion counts, error rates, and scan duration.
  - Ensure worker and store idempotency:
    - Worker writes must be idempotent; use outbox idempotency or monotonic LastUpdatedUtc checks to prevent regressions.
- SQL → Outbox pattern:
  - Add Outbox table and repository; ensure Device metadata updates write an Outbox entry in the same SQL transaction when replication to Table Storage is required (metadata only).
  - `SqlToTableBackfill` tool: OPTIONAL — implement under `src/ProdControlAV.Infrastructure/Tools/SqlToTableBackfill` only if you need to populate Table Storage from SQL for initial production migration; in test/staging scenarios where Table Storage is already seeded, this tool can be omitted or left as an opt-in utility.
- Background worker / Azure Function:
  - Poll queue and apply upserts to Table Storage with idempotency and retry.
  - Move failed items to DLQ after N attempts and emit alert.
  - Ensure worker validates tenantId and monotonic LastUpdatedUtc from messages before applying merges to avoid regressions.
- Reconciliation & Migration:
  - Add scheduled reconciliation background job (configurable cadence per environment).
- API controllers/services:
  - Ensure all code paths that would write status to SQL are updated to publish status messages to the queue instead. Dashboard endpoints read from Table Storage only.
- Tests:
  - Unit tests for TableDeviceStore (Merge vs Replace), Outbox generation, worker idempotency.
  - Integration tests for migration and reconciliation (use dedicated test storage account).
- Operational:
  - Configure Storage Account with private endpoint, diagnostic logs, and assign Managed Identity roles.
  - Create dashboards and alerts in Azure Monitor / App Insights for the metrics listed above.
- Agent changes:
  - Replace direct SQL status writes with queue publish (Managed Identity). Add tests to validate publish.
  - Agent messages must include tenantId, deviceId, LastUpdatedUtc, and an idempotency key; workers must validate tenantId before writing to Table Storage.
- Worker changes:
  - Upsert/Merge status to Table Storage; ensure retention enforcement is observed by scheduled deletion job.
- SQL changes:
  - Remove & stop updating high-frequency status columns.
- Migration:
  - Backfill Table Storage, verify parity, then cutover agents.
