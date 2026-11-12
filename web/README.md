# MicroCRM web

React + TypeScript single-page app (Vite, TanStack Query, React Router in declarative mode) for the MicroCRM contacts API. See `docs/architecture.md` (web routes and data flow) and `docs/conventions.md` (Frontend) at the repository root.

All commands run from the **repository root** and are defined in `.claude/harness.env`.

| Task | Command |
|---|---|
| Run the API | `dotnet run --project src/MicroCrm.Api --urls http://localhost:5080` (`DEV_API_CMD`) |
| Run the web app | `npm --prefix web run dev` (`DEV_WEB_CMD`; Vite serves on http://localhost:5173) |
| Test | `npm --prefix web test` (`FRONTEND_TEST_CMD`; Vitest, one run) |
| Test one file | `npm --prefix web test -- ContactForm` |
| Watch tests | `npm --prefix web run test:watch` |
| Lint | `npm --prefix web run lint` (oxlint, config in `web/.oxlintrc.json`) |
| Typecheck | `npm --prefix web run typecheck` (`tsc -b`) |
| Build | `npm --prefix web run build` (typecheck, then `vite build` into `web/dist`) |
| Format | `npm --prefix web run format` (Prettier) |

## The `/api` proxy
The app only calls relative `/api/...` URLs. In development, Vite proxies `/api` to the API (`server.proxy` in `vite.config.ts`). The target is `http://localhost:5080` by default; set the `API_URL` environment variable to point somewhere else:

```
API_URL=http://localhost:6000 npm --prefix web run dev
```

Start the API first, otherwise list and save requests fail. Tests don't need the API: they mock the network with MSW (`src/test/`).

## Production
`npm --prefix web run build` produces static files in `web/dist`. The host must serve `index.html` for unknown paths so deep links such as `/contacts/new` work. Deployment isn't decided yet (ADR-0010).
