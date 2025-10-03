# ProdControlAV Azure Table Storage Migration Status

## Change Log

- [x] Workspace analysis and planning completed (2025-10-03)
- [x] Created IDeviceStatusStore interface and DeviceStatusDto record in infrastructure
- [x] Implemented TableDeviceStatusStore for Azure Table Storage
- [x] Registered TableServiceClient, TableClient, and TableDeviceStatusStore in API DI
- [x] Refactored StatusController to use new repository, DTOs, and enforce tenant isolation
- [x] Added Table transaction logging in controller
- [x] Updated API configuration files for Azure and Azurite endpoints
- [x] Updated WebApp and Agent for new API contract
- [x] Added and passed unit tests for TableDeviceStatusStore and StatusController
- [x] Resolved all build errors and warnings

## Implementation Plan (Next Steps)

1. [ ] Add integration tests for Table Storage using Azurite
2. [ ] Update README with test coverage, integration requirements, troubleshooting, and rollout notes

## Test Coverage Summary
- Unit tests cover:
  - TableDeviceStatusStore: partitioning, upsert logic, query isolation
  - StatusController: POST/GET endpoints, claim validation, multi-tenant isolation
- All tests passing; build is clean

## Integration Requirements
- WebApp and Agent must use new API contract:
  - POST /api/status: StatusPostDto
  - GET /api/status?tenantId=...: StatusListDto
- API configuration must specify Table Storage endpoint (Azure or Azurite)

## Troubleshooting Notes
- If Table Storage errors occur, check endpoint/connection string in appsettings.json
- For local dev, use Azurite and ensure port 10002 is open
- Monitor API logs for Table transaction counts and latency

## Rollout Guidance
- Validate in dev/staging with Azurite before production cutover
- Monitor dashboard read latency and Table transaction counts
- Optional: implement StatusHistory table in phase 2

## Open Questions
- Do any legacy consumers (WebApp/Agent) require further DTO or endpoint compatibility adjustments?
- Any additional claims or security checks needed for agent POSTs?

## Notes
- All business logic for device status is now routed through Azure Table Storage with partitioned queries for multi-tenant isolation.
- Local development is supported via Azurite; production uses Azure Table Storage.
- Table transaction logging is enabled for observability and cost tracking.
