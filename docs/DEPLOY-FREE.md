# A free demo online

For showing TillKnowIT to people with **made-up data**. It is not for real shops: free hosting sleeps, has small limits, and has no backups you can count on.

| Part | Where | Cost |
| --- | --- | --- |
| The 7 apps | GitHub Pages (`.github/workflows/pages.yml`) | Free (the repository is public) |
| The API | Render free web service (`render.yaml`, `backend/Dockerfile`) | Free; sleeps after about 15 minutes idle, so the first request takes up to a minute |
| The database | SQL Server: see "The database" below | The one part with no free option outside Azure |

## 1. The apps on GitHub Pages

1. On GitHub: repository **Settings > Pages > Build and deployment > Source: GitHub Actions** (one time).
2. After the API exists (step 2), add a repository variable: **Settings > Secrets and variables > Actions > Variables > New variable**, name `API_URL`, value the API's address with no trailing slash, for example `https://tillknowit-api.onrender.com`.
3. Push to `main`, or run the workflow "Publish the apps to GitHub Pages" by hand. The apps appear at `https://takatsot.github.io/TillKnowIT-/` with a page that links to each one.

A push to `frontend/` republishes everything. The apps are built with `VITE_API_URL` = `API_URL`; without it they look for the API on the same address as the page and every request fails.

## 2. The API on Render

1. render.com > New > Blueprint > choose this repository. It reads `render.yaml`.
2. When asked, paste `ConnectionStrings__Database` (step 3). The other keys are generated for you.
3. If the service is not called `tillknowit-api`, change `Slips__BaseUrl` to match the address Render gives it.
4. The first start creates the tables and the demo people and shop (a few minutes). The people and phone numbers are on the landing page; the sign-in code is shown under the phone box because no SMS is sent.

`Demo__Enabled=true` is what does that. **Never switch it on anywhere that holds real people's data**: it shows every sign-in code on screen, so anyone can sign in as anyone.

The Docker image itself has not been built on the machine this was written on (Docker was not running); the `dotnet publish` step it uses was, and the same settings were run as a plain process in Production mode with Demo on (health, CORS and sign-in all answered correctly). If the Render build fails, the build log will say why.

## 3. The database

TillKnowIT is built on **SQL Server**. Free SQL Server hosting that works from outside your own PC exists in one place:

- **Azure SQL Database free offer** (serverless, pauses when idle, a monthly allowance of compute and 32 GB). It needs an Azure subscription, usually with a card on file; check Microsoft's current limits before relying on it. Create it, add a firewall rule that allows Azure services (and Render's addresses), and use its ADO.NET connection string as `ConnectionStrings__Database`.

Not options: Neon, Supabase, PlanetScale and the other free databases are PostgreSQL or MySQL; moving to PostgreSQL means redoing every module's migrations and retesting everything. Free VMs are too small to run SQL Server (it wants about 2 GB of memory).

Keep it honest: if there is no Azure subscription, the free choices are (a) run the API and database on your own PC and publish them with a free tunnel such as Cloudflare Tunnel, or (b) the PostgreSQL port.

## Before real shops use it

Replace the demo mode with real settings: a paid always-on API and database with backups, the SMS provider (`Sms:BulkSms:*`), `Demo__Enabled` off, a sign-up limit per address, and the legal, security and load reviews in `docs/STATUS.md`.
