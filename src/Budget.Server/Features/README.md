# Features

Server code is organised by feature (vertical slices, see ADR-004). Each feature gets its own folder:

```
Features/
  Households/
    HouseholdEndpoints.cs      // MapGroup("/api/households") and the endpoints
    RenameHousehold.cs         // request/response, validator and handler together
  Invitations/
  IncomeStreams/
```

Guidelines:

- One `Map<Feature>Endpoints(this IEndpointRouteBuilder app)` extension per feature, called from `Program.cs`.
- Household-scoped routes use `/api/households/{householdId}/...` and an authorisation policy
  (`HouseholdViewer`, `HouseholdUser`, `HouseholdAdmin`, `HouseholdOwner`).
- Every household-scoped endpoint gets a cross-household integration test.
- Request/response DTOs that the client also uses go in `Budget.Shared`.
