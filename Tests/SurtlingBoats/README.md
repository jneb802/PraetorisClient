# Surtling boat fuel checks

Run with a .NET 8 or newer SDK and runtime:

```sh
dotnet run --project Tests/SurtlingBoats/SurtlingBoats.csproj
```

The executable compiles the production `SurtlingBoatFeature.cs` with small game substitutes. It checks fuel priority, one-item consumption, active-fuel boosts and icons, refill fallback, saved fuel compatibility, ownership changes, and free-fuel behavior.

The substitutes do not validate actual Unity rendering, ship physics, network transport, or production configuration synchronization. Complete those checks in a live Praetoris Season 8 test environment before deployment.
