# Roomy
A simple room booking application built in dotnet

## Documentation

- [Product Requirements (PRD)](docs/product-requirements.md) — what we're building and why
- [Technical Design](docs/technical-design.md) — architecture, data model, and implementation approach
- [Development guide](docs/development.md) — repo layout, local setup, and conventions

## Quick start

```bash
docker compose up -d postgres
dotnet run --project src/Roomy.Api     # API + Swagger at /swagger
cd frontend && npm ci && npm start -- member
```
