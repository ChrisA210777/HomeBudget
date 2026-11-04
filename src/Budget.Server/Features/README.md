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
- Household-scoped routes live under `HouseholdRoutes.Base` (`/api/households/{householdId:guid}`) and
  require one of the `HouseholdPolicies`: `Viewer`, `User`, `Admin` or `Owner`. Non-members get 404,
  members without the role get 403. Once authorised, `ICurrentHousehold` is set and every
  `IHouseholdOwned` query is filtered to that household.
- Every household-scoped endpoint needs two tests from `IntegrationTestBase`:
  `AssertOtherHouseholdsCannotAccessAsync` (isolation) and `AssertMinimumRoleAsync` (roles).
- Request/response DTOs that the client also uses go in `Budget.Shared`.
