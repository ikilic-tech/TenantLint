; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TL001   | TenantIsolation | Warning  | IgnoreQueryFilters() removes tenant filter
TL002   | TenantIsolation | Warning  | Raw SQL references tenant table without tenant filtering
TL003   | TenantIsolation | Warning  | Dapper raw SQL references tenant table without tenant filtering
TL005   | TenantIsolation | Warning  | Tenant-scoped entity has no query filter
TL102   | TenantIsolation | Info     | Raw SQL references tenant table with possible tenant filtering
TL103   | TenantIsolation | Info     | Dapper raw SQL references tenant table with possible tenant filtering
TL105   | TenantIsolation | Info     | Tenant-scoped entity may be missing a query filter (dynamic filter detected)
TL900   | TenantIsolation | Info     | Dynamic query filter detected
TL901   | TenantIsolation | Info     | CrossTenant attribute should state a reason
TL902   | TenantIsolation | Warning  | Class-level CrossTenant suppresses all methods
