# Deployment — Docker + Jenkins (single Linux server, Stub MT5)

Two independent stacks, **one compose each**:

| Stack | Repo / compose | Contains |
|---|---|---|
| **Backend** | `mondshield-api/docker-compose.yml` | Postgres + .NET API |
| **Frontend** | `mondshield-web/docker-compose.yml` | Next.js web app |

They're decoupled: the browser calls the API directly at its public URL, so the frontend only
needs to know that URL — no shared docker network. Deploy them separately (separate Jenkins jobs).
MT5 runs in **Stub** mode (the live MT5 DLLs are Windows-only — see the last section).

## Prerequisites (on the server)
Docker Engine + the Compose plugin (`docker compose version`).

---

## Backend (`mondshield-api`)

```bash
cd mondshield-api
cp .env.example .env      # set SERVER_HOST, POSTGRES_*, JWT_SIGNING_KEY, SEED_ADMIN_*
docker compose up -d --build
docker compose ps
```

- **API** → `http://<SERVER_HOST>:<API_PORT>` (default 5259); Swagger at `/swagger`.
- On first start it auto-creates the schema (no migrations yet) and seeds the admin + `user1`/`user2`.
  Data persists in the `pgdata` volume. Postgres isn't published to the host by default.
- Stop: `docker compose down` (add `-v` to also wipe the DB volume).

## Frontend (`mondshield-web`)

```bash
cd mondshield-web
cp .env.example .env      # set SERVER_HOST, API_PORT (must match the backend), WEB_PORT
docker compose up -d --build
```

- **Web** → `http://<SERVER_HOST>:<WEB_PORT>` (default 3000).

### Important: `SERVER_HOST`
The browser talks to the API directly (tokens live in the browser), so
`NEXT_PUBLIC_API_BASE_URL` is baked into the web image at build time as
`http://<SERVER_HOST>:<API_PORT>`. `SERVER_HOST` must be the address **your browser** can reach —
your server's public IP or domain, **not** `localhost` and **not** an internal docker name. If you
change it, rebuild the web image (`docker compose up -d --build`).

The backend's `.env` also has `SERVER_HOST` + `WEB_PORT` — used to set the CORS origin
(`http://<SERVER_HOST>:<WEB_PORT>`) so the API accepts calls from the frontend. Keep the two
`.env` files consistent.

---

## Jenkins (one job per stack)

Each repo has its own `Jenkinsfile`. Create two Pipeline jobs.

| Job | Repo | Secret-file credential (the filled-in `.env`) |
|---|---|---|
| mondshield-api | `mondshield-api` | id **`mondshield-api-env`** |
| mondshield-web | `mondshield-web` | id **`mondshield-web-env`** |

Each job: checkout → drop the `.env` in → `docker compose up -d --build`. The agent needs Docker +
Compose and access to the deploy host's Docker (simplest: run the agent **on the server**).

---

## Notes

- **Secrets:** `.env` files are git-ignored. The backend's baked `appsettings.json` still holds dev
  defaults (incl. a placeholder MT5 password Stub never uses); compose env overrides the
  connection string, JWT key, CORS origin, and MT5 mode. Set strong values in `.env`.
- **Environment = Development in the container:** deliberate — enables auto schema-create (no
  migrations yet) + seeding, and disables HTTPS redirect so plain HTTP works. Switch to Production
  once migrations are introduced.
- **TLS / domain:** for real use, put a reverse proxy (Caddy/Nginx/Traefik) in front for HTTPS,
  then set `SERVER_HOST` to your domain and the API's CORS origin to the HTTPS web URL.

## Going Live with MT5 later (needs Windows)

The MT5 Manager API is Windows-native, so the **Linux backend container can only run Stub**. To use
live MT5 you must run the **backend on a Windows host** (natively or a Windows container) with
`Mt5:Mode=Live` + the `libs/mt5` DLLs. Postgres and the frontend stay in Docker anywhere; only the
API moves to Windows. See `docs/mt5-integration.md`.
