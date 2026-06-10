# Roomy
A simple room booking application built in dotnet

## Documentation

- [Product Requirements (PRD)](docs/product-requirements.md) — what we're building and why
- [Technical Design](docs/technical-design.md) — architecture, data model, and implementation approach
- [Development guide](docs/development.md) — repo layout, local setup, and conventions

## Quick start

Everything in Docker:

```bash
docker compose up --build
# web: http://localhost:8080  (admin at /admin/, kiosk at /kiosk/)
# api: http://localhost:5023  (also proxied at :8080/api and :8080/swagger)
```

Or for development with fast feedback:

```bash
docker compose up -d postgres
dotnet run --project src/Roomy.Api     # API + Swagger at localhost:5023/swagger
cd frontend && npm ci && npm start -- member
```
