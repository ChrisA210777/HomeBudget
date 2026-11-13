# End-to-end and accessibility tests (BUD-40)

Playwright tests that drive the real app in Chromium at a phone size (390×844) and a desktop size (1280×800):

- `shell.spec.ts`: navigation, no sideways scrolling, 44px touch targets, signed-out view
- `theme.spec.ts`: light, dark and device themes
- `accessibility.spec.ts`: an axe-core scan of each main page in both themes, failing on serious or critical problems
- `keyboard.spec.ts`: the main flows using only the keyboard
- `pwa.spec.ts`: the manifest and icons, offline start-up, and no household data in the offline cache
- `screenshots.spec.ts`: saves screenshots to `screens/` for review

## How they sign in

The app is published and run in the **Testing** environment with `TestSignIn__Enabled=true`. This maps `/account/test-sign-in?name=...`, which signs in as that person without Microsoft, creating them and their household on first use. The endpoint doesn't exist in any other environment.

## Run locally

```bash
docker compose up -d   # SQL Server
dotnet publish src/Budget.Server -c Release -o artifacts/server
cd artifacts/server
ASPNETCORE_ENVIRONMENT=Testing TestSignIn__Enabled=true Database__MigrateOnStartup=true \
  ConnectionStrings__Budget="Server=localhost,1433;Database=HomeBudgetE2E;User Id=sa;Password=HomeBudget_Dev_Pa55!;TrustServerCertificate=True;Encrypt=False" \
  dotnet Budget.Server.dll --urls https://localhost:7180

# in another terminal
cd tests/e2e
npm ci                      # installs exactly what package-lock.json says
npx playwright install chromium
npx playwright test
```

CI runs the same steps in the `e2e` job.

The versions are locked in `package-lock.json` (BUD-160), and Dependabot proposes updates weekly. To change a version,
edit `package.json` and run `npm install` to update the lockfile, then commit both.
