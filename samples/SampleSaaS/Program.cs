// SampleSaaS — build this project to see TenantLint diagnostics.
// Run: dotnet build samples/SampleSaaS/SampleSaaS.csproj
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "SampleSaaS: build output shows TenantLint diagnostics for deliberate violations.");
app.Run();
