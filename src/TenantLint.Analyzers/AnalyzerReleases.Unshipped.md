### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TL000   | TenantIsolation | Disabled | Placeholder rule
TL001   | TenantIsolation | Warning  | IgnoreQueryFilters() removes tenant filter
TL002   | TenantIsolation | Warning  | Raw SQL references tenant table without tenant filtering
TL003   | TenantIsolation | Warning  | Dapper raw SQL references tenant table without tenant filtering
TL005   | TenantIsolation | Warning  | Tenant-scoped entity has no query filter
TL102   | TenantIsolation | Info     | Raw SQL references tenant table with possible tenant filtering
TL103   | TenantIsolation | Info     | Dapper raw SQL references tenant table with possible tenant filtering
TL105   | TenantIsolation | Info     | Tenant-scoped entity may be missing a query filter (dynamic filter detected)
TL900   | TenantIsolation | Info     | Dynamic query filter detected
TL901   | TenantIsolation | Info     | CrossTenant attribute should state a reason
TL010   | TenantIsolation | Warning  | Tenant-scoped entity bound from request body
TL011   | TenantIsolation | Warning  | Pooled DbContext has tenant filter on constructor-only field
TL111   | TenantIsolation | Disabled | Tenant filter references constructor-only field (unsafe if pooled)
TL012   | TenantIsolation | Warning  | HasQueryFilter references a value not rooted in the DbContext instance
TL013   | TenantIsolation | Warning  | Raw ADO.NET SQL references tenant table without tenant filtering
TL113   | TenantIsolation | Info     | Raw ADO.NET SQL references tenant table with possible tenant filtering
TL902   | TenantIsolation | Warning  | Class-level CrossTenant suppresses all methods
