# Davetiye — Agent Instructions

## Product Source of Truth

Before planning, designing, or implementing any feature, read:

- `docs/PRODUCT.md`

`docs/PRODUCT.md` defines the current product scope and requirements.

Do not invent product features that are not described there.
If a product decision is unclear or missing, ask the user instead of making a major assumption.

## Project Goal

Build a production-ready responsive digital invitation web application.

The planned core stack is:

- Frontend: React
- Backend: ASP.NET Core Web API / .NET
- Database: PostgreSQL
- Media: Cloudflare services
- Hosting: Hostinger VPS
- Version Control: Git

## Development Principles

- Keep the architecture maintainable and modular.
- Prefer simple solutions over unnecessary abstractions.
- Do not introduce a microservice architecture unless explicitly requested.
- Never hardcode configurable business limits when they belong in plans or system settings.
- Security and authorization must be enforced on the backend.
- Public invitation identifiers must never grant Creator permissions.
- Never store authentication secrets or credentials in the repository.
- The application must be responsive on mobile, tablet, and desktop.
- Do not implement features listed as MVP-out/backlog unless explicitly requested.

## Working Rules

Before implementing a substantial feature:

1. Read the relevant product requirements.
2. Inspect the existing codebase.
3. Identify dependencies and affected areas.
4. Create or update the implementation plan when appropriate.
5. Implement the smallest coherent solution.
6. Run relevant tests/builds.
7. Review the resulting changes before declaring the task complete.

Do not rewrite unrelated working code.

Do not silently change established product decisions.

If implementation requires changing the product scope, stop and explain the issue first.