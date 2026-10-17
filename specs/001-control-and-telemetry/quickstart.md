# Quickstart: VFilter 001

```bash
dotnet build -warnaserror
dotnet test                                   # unit tests
export VFILTER_TEST_CONNECTION="Server=127.0.0.1,14333;User Id=sa;Password=…;TrustServerCertificate=true"
dotnet test --filter Category=SqlServer       # integration against the local container
VFILTER__ConnectionString="…;Database=VFILTER" dotnet run --project src/VFilter.Host
```
End to end: run the website (`php artisan migrate`, `vfilter:install-defaults`, enable modules) against the
same SQL Server, start the filter, and check that the dashboard shows Online, a Restart goes Done, and a gateway
mode switch shows on the card.
