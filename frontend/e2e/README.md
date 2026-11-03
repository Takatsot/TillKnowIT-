# Browser tests

These click through every app in a real browser (Playwright) against the real API and SQL Server. They are **not** part of `npm test`, because they need the whole thing running.

What they check:

- every menu section of the back office, supplier portal, admin, parent app and insights portal, and every tab inside each, at desktop and phone width: nothing throws, nothing draws blank, nothing runs off the side;
- the till: register with the one-time setup code, a wrong PIN, sell by barcode, take cash, and that the sale reaches the server; every tab opens; it fits a phone;
- signing in through the real sign-in screen.

## Run them

1. Start the API in Development against SQL Server on a database that has not been seeded before (the API makes the demo people, a shop with a till and products, a supplier and a school the first time it starts). With SQL Server LocalDB:

   ```
   cd backend/src/Host/TillKnowIT.Api
   set ASPNETCORE_ENVIRONMENT=Development
   set ConnectionStrings__Database=Server=(localdb)\MSSQLLocalDB;Database=tillknowit_dev;Trusted_Connection=True;TrustServerCertificate=True
   dotnet run --no-launch-profile --urls http://localhost:5080
   ```

2. From `frontend/e2e`: `npm install` once, then `npm test`. The six apps are started for you if they are not already running. `npm run test:ui` opens Playwright's own window; `npm run report` shows the last report.

The browser is the Chromium Playwright already downloaded, or set `PW_CHROME` to a Chrome or Edge `.exe`. `API_URL` overrides `http://localhost:5080`.

Sign-in codes are limited to five an hour per number, so the tests sign each person in once and keep the session in `.session-cache.json` (not committed). If the limit is hit, clear it with `delete from [identity].otp_challenges` in the dev database.
