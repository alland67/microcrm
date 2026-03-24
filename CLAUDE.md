# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MicroCrm is a full-stack CRM application with:
- **Backend**: ASP.NET Core (.NET 10) following Clean Architecture with CQRS
- **Frontend**: Angular 14 SPA

## Commands

### Backend (from repo root or `src/backend/`)

```bash
# Build the solution
dotnet build MicroCrm.slnx

# Run the API (starts on http://localhost:5004 / https://localhost:7004)
dotnet run --project src/backend/MicroCrm.WebAPI

# Run tests (when test projects exist)
dotnet test MicroCrm.slnx

# EF Core migrations (from Infrastructure project)
dotnet ef migrations add <MigrationName> --project src/backend/MicroCrm.Infrastructure --startup-project src/backend/MicroCrm.WebAPI
dotnet ef database update --project src/backend/MicroCrm.Infrastructure --startup-project src/backend/MicroCrm.WebAPI
```

### Frontend (from `src/frontend/micro-crm/`)

```bash
npm install
ng serve           # Dev server at http://localhost:4200
ng build           # Production build to dist/micro-crm
ng test            # Unit tests via Karma/Jasmine
ng test --include="**/foo.spec.ts"  # Run a single test file
```

## Architecture

### Backend — Clean Architecture (4 layers)

Dependencies flow inward: `WebAPI → Application → Domain`, `Infrastructure → Application + Domain`

- **Domain** (`MicroCrm.Domain/`): Entities, value objects, domain events, interfaces, exceptions. No external dependencies.
- **Application** (`MicroCrm.Application/`): CQRS features (commands/queries), pipeline behaviors, mapping profiles, application interfaces. References Domain only.
- **Infrastructure** (`MicroCrm.Infrastructure/`): EF Core persistence (configurations, migrations, repositories), identity, external services. Implements interfaces defined in Application/Domain.
- **WebAPI** (`MicroCrm.WebAPI/`): Controllers, middleware, filters, `Program.cs` (minimal hosting model). References Application and Infrastructure.

The Application layer is structured around CQRS — new features go in `Application/Features/<FeatureName>/` with separate Commands and Queries folders. Pipeline behaviors (validation, logging, etc.) live in `Application/Common/Behaviors/`.

### Frontend — Angular 14

Standard Angular module-based architecture with `AppModule` bootstrapping `AppComponent`. Routing is configured via `AppRoutingModule`. Feature modules should be lazy-loaded via the router.

Environment-specific config lives in `src/environments/environment.ts` (dev) and `environment.prod.ts` (prod).

### API

The WebAPI exposes a REST API documented via OpenAPI/Swagger. The `.http` file at `src/backend/MicroCrm.WebAPI/MicroCrm.WebAPI.http` can be used for manual endpoint testing.
