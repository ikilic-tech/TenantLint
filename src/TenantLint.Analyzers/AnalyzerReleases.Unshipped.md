### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TL000   | TenantIsolation | Disabled | Placeholder rule
TL001   | TenantIsolation | Warning  | IgnoreQueryFilters() removes tenant filter
TL002   | TenantIsolation | Warning  | Raw SQL references tenant table without tenant filtering
TL003   | TenantIsolation | Warning  | Dapper raw SQL references tenant table without tenant filtering
TL900   | TenantIsolation | Info     | Dynamic query filter detected
TL901   | TenantIsolation | Info     | CrossTenant attribute should state a reason
TL902   | TenantIsolation | Warning  | Class-level CrossTenant suppresses all methods
