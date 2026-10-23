### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TL004   | TenantIsolation | Warning  | Tenant identity received from client input
TL104   | TenantIsolation | Info     | Tenant identity received from route parameter
TL006   | TenantIsolation | Warning  | Cache key does not include tenant identifier
TL007   | TenantIsolation | Warning  | Tenant context stored in static field
TL008   | TenantIsolation | Info     | Background service uses tenant-scoped DbContext without explicit scope
TL009   | TenantIsolation | Warning  | RLS session variable uses session scope instead of transaction-local
TL010   | TenantIsolation | Warning  | Tenant-scoped entity bound from request body
TL011   | TenantIsolation | Warning  | Pooled DbContext has tenant filter on constructor-only field
TL111   | TenantIsolation | Disabled | Tenant filter references constructor-only field (unsafe if pooled)
TL012   | TenantIsolation | Warning  | HasQueryFilter references a value not rooted in the DbContext instance
TL013   | TenantIsolation | Warning  | Raw ADO.NET SQL references tenant table without tenant filtering
TL113   | TenantIsolation | Info     | Raw ADO.NET SQL references tenant table with possible tenant filtering
TL014   | TenantIsolation | Warning  | ABP tenant-filter bypass
TL015   | TenantIsolation | Warning  | Finbuckle TenantMismatchMode.Ignore allows cross-tenant writes
TL201   | TenantIsolation | Warning  | #pragma warning disable TL rule without justification
TL202   | TenantIsolation | Warning  | [SuppressMessage] targeting TL rule without justification
TL203   | TenantIsolation | Warning  | Tenant isolation rules globally disabled
