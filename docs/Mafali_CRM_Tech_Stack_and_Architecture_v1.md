# Mafali CRM — Technology Stack & Architecture Recommendation v1

This proceeds without answers to the three open questions raised earlier (team skillset, hosting constraints, budget/support expectations). Every choice below is a specific, defensible default rather than a placeholder — but three of them (marked ⚠) would likely change based on those answers. Everything else should hold regardless.

**Governing principle, stated once rather than repeated per section:** this is a moderate-complexity, internal, CRUD-heavy line-of-business application for a small team (evidence from the source: a handful of named staff — GDM, BLJ, YHB — across the whole 1600-page history, no indication of high concurrency, no real-time or streaming needs, no heavy computation). Every recommendation below optimizes for simplicity, maintainability, and low operational cost over scalability headroom the app will never need.

**Revision note:** an earlier version of this document included CI/CD, cloud object storage, and third-party monitoring/error-tracking by default. Revised below to cut all three — they were operational conveniences, not things this project is at risk without. The line drawn throughout this document: keep anything that's cheap and closes a real gap this review actually found (password hashing, enforced database constraints, tests for logic that's shown itself risky to get wrong); drop anything that just adds infrastructure or process without fixing a specific identified problem. Everything cut below has a one-line "add this later if X happens" — none of it is a decision that's expensive to reverse.

---

## 1. Architecture style

**Recommendation: a modular monolith.** One deployable backend application, internally organized into clear domain modules (clients, interactions, revenue, filters, reference-data, auth), talking to a single PostgreSQL database. Not microservices, not a distributed system of any kind.

**Why not microservices.** Microservices solve organizational problems (independent teams shipping independently) and scaling problems (different parts of a system needing to scale differently) — neither applies here. A small team building a CRM for one business would spend more time on network calls, distributed transactions, and deployment orchestration than on the actual product. This isn't a judgment call so much as a mismatch of tool to problem.

**Why not serverless/function-per-endpoint.** Cold-start latency and the operational complexity of managing many small functions aren't worth it for a system with steady, predictable, low-volume traffic from internal staff. A single long-running server process is simpler to reason about, debug, and monitor.

**API style: conventional REST, not GraphQL.** The frontend's data needs are well-understood and don't involve deep, client-driven nested queries — GraphQL's main advantage over REST doesn't apply here, and it adds a real learning-curve and tooling cost. Plain REST (or RPC-style endpoints where that reads more naturally, e.g. `POST /clients/:id/apply-interaction`) is easier to test, cache, log, and debug, and easier for a future developer to onboard into.

---

## 2. Language & backend framework

**Recommendation: TypeScript, on both backend and frontend.** One language across the whole stack is a genuine, concrete win for a small team: shared types between API and UI (catching mismatches at compile time instead of in production), one hiring pool to draw from, one set of tooling conventions to learn.

⚠ **This is the first place team skillset changes the answer.** If whoever builds and maintains this already has deep expertise in a different ecosystem (e.g. .NET, given the Windows/HFSQL lineage of the legacy system, or Python), that expertise should generally win over my default — "ease of onboarding future developers" means onboarding into what the *actual* team knows, not an abstractly popular choice.

**Backend framework: Fastify.** Mature, fast, good TypeScript support, schema-based request validation built in, minimal ceremony. I'm rejecting **NestJS** specifically because its dependency-injection/decorator-heavy architecture is designed for larger teams and more complex domains than this one — it would add structure this app doesn't need to earn. I'm rejecting plain **Express** because Fastify gives the same simplicity with better TypeScript ergonomics and built-in validation, for no real cost.

---

## 3. Frontend

**Recommendation: React + Vite, as a separate single-page application from the API**, not a full-stack meta-framework like Next.js.

**Why a real SPA framework matters here specifically:** the legacy app's core daily-use screens (Fen_Recherche_Client, Fen_Parcours_Client, Fen_Filtre, every "Fichier_X" grid) are dense, sortable, filterable, multi-column data tables with inline editing — that *is* the product, from the user's point of view. Replicating that experience well is central to the stated goal ("staff should already know how to use it"), and a proper data-grid component (**TanStack Table**, headless and flexible, paired with **TanStack Query** for data fetching/caching) is the most direct way to deliver dense, sortable, filterable tables that feel like the desktop app staff already know — better than trying to approximate that with simpler tooling.

**Why not Next.js/Remix (a combined frontend+backend framework):** their main strengths — server-side rendering for SEO, hybrid rendering strategies — don't matter for an internal tool with no public-facing pages and no SEO need. Keeping the API and the frontend as separate deployable concerns is a more conventional, more easily testable, more easily hireable setup for this kind of app, and it keeps backend business logic out of a framework whose conventions are oriented around page rendering.

**Forms:** React Hook Form + a schema validator (Zod) shared between frontend and backend validation — one source of truth for "what does a valid client record look like," instead of the legacy pattern of validation logic scattered and duplicated per screen.

---

## 4. Database & ORM

**Database: PostgreSQL** — already the target of the Database Design v1 document; reliable, free, strong constraint support (which matters specifically here, replacing a legacy schema with zero enforced relationships), excellent tooling.

**ORM: Prisma.** Type-safe queries generated from the schema, a clean migration workflow, and it directly produces the TypeScript types the frontend can share. I'm rejecting a raw query builder (Knex) because it gives up type safety for flexibility this app doesn't need, and rejecting TypeORM because Prisma's tooling and documentation are generally stronger for a team that isn't already invested in TypeORM specifically.

---

## 5. Authentication & authorization

**Authentication: self-hosted session-based auth**, passwords hashed with **argon2** (or bcrypt as a well-understood fallback), sessions stored server-side (Postgres or Redis). This directly replaces the legacy plaintext-password system — non-negotiable, per the risk register.

**Why not a third-party identity platform (Auth0, Clerk, etc.):** they're a legitimate option, but add a recurring cost and an external dependency for a problem (login for a small internal team) that a well-implemented session system solves completely on its own. I'd revisit this if SSO becomes a real requirement.

⚠ **This is the second place an open question matters.** If LDAP/SSO integration is confirmed to be genuinely needed (the legacy system supported it, but I couldn't confirm it's actually used for Mafali), that changes this recommendation — either toward a proper SSO protocol (SAML/OIDC) integration or, if a specific identity platform is already in use elsewhere at AHG, integrating with that instead of building bespoke session auth.

**Authorization: role-based, feature/screen-level** (matching the `roles.permissions` JSONB design from the database schema) enforced via backend middleware checking the current user's role permissions before each request — not the legacy's per-UI-element granularity, per the reasoning already given in the database design.

---

## 6. Background jobs, file storage, Excel processing

**Background jobs: none, initially.** The only genuinely bulk operation in the legacy system is Excel import/export, and the legacy UI's own pattern (a progress bar with the user watching it complete) suggests synchronous, foreground processing was acceptable. Recommend keeping it that way — a simple synchronous endpoint with a progress indicator — rather than standing up a job queue for one feature. If import files turn out to be large enough to time out a request, add **BullMQ + Redis** at that point; introducing a job queue before there's a proven need would be complexity bought on credit.

**File storage: plain files on the server's disk**, referenced by a file path column in Postgres — not object storage. For the volume of attachments this app deals with (occasional signed quotes/orders per interaction, not a media-heavy application), a folder on disk is simpler to set up, has nothing extra to run, and is trivial to back up alongside the rest of the server. Object storage (S3/MinIO) is a fine upgrade later if attachment volume grows or the app moves to multiple servers — not something to set up on day one for a problem that doesn't exist yet.

**Excel import/export: `exceljs`** (a mature, well-maintained Node library) for both directions. No reason to build custom parsing — this is a solved problem. The new import feature should be **one** clean upsert-by-key flow, combining the best ideas found across the legacy's four overlapping mechanisms (match-by-ID upsert semantics from Fen_Update, the "explicit clear vs. leave-unchanged" sentinel convention from FEN_Modification_Depuis_Excel), rather than reproducing all four.

---

## 7. Deployment, CI/CD, hosting

**Containerization: Docker**, one image for the backend, one for the frontend (or served as static files from the backend/a CDN) — not Kubernetes. At this scale, container orchestration is pure overhead; a single container (or two) running on a single host, or a simple managed platform, is sufficient and dramatically easier to operate.

⚠ **This is the third place an open question matters, and the biggest swing factor in this whole document.** The legacy system runs entirely on AHG-managed on-premise infrastructure (`\\ARBAS`, `\\CANIGOU` servers, a shared database config file). If the new system needs to stay on-prem for the same reasons, the recommendation is: Docker Compose on a single AHG-managed server, PostgreSQL running alongside or on a dedicated DB server, backups via scheduled `pg_dump`. If cloud hosting is acceptable, a small managed platform (Render, Railway, Fly.io, or a single managed VM on any standard cloud provider) with a managed Postgres instance (automated backups, point-in-time recovery included) is simpler to operate and recommended instead. **I don't have enough information to pick between these yet, but the application architecture above works identically either way** — this only affects the deployment target, not the design.

**CI/CD: none, initially.** Run tests locally before deploying, deploy by hand (`docker compose pull && up`, or equivalent). For a small team shipping infrequently, an automated pipeline is process bought for a problem you don't have yet — it's cheap to add later (a single config file) the moment deploys become frequent enough, or the team grows enough, that manual steps start being where mistakes happen. Not needed to get this system built and running.

---

## 8. Testing, logging, monitoring, caching

**Testing: Vitest for the business logic that's actually risky to get wrong** — interaction/history saving, permission checks, the import upsert logic — run locally (`npm test`) before deploying, no pipeline required. This isn't "fancy," it's the cheapest possible insurance against the kind of silent data bug this review found repeatedly in the legacy system, and it costs nothing beyond writing the tests. Full end-to-end browser testing (Playwright) is worth skipping for now — real setup and maintenance overhead for a small team, not worth it before there's a second developer or a track record of regressions slipping through.

**Logging: plain structured output to a file** (or just stdout, redirected to a file the server rotates) — no log aggregation service. If something breaks, whoever's maintaining the app reads the log file. That's sufficient at this scale, and `pino` (or even the standard library's console) is enough to make those logs readable.

**Monitoring: none, beyond checking logs when something seems wrong.** No Sentry, no uptime service, no dashboard. If the app becomes hard to keep an eye on informally — more users, harder-to-reproduce bugs — a free-tier error tracker is a five-minute addition at that point, not a foundational decision to make now.

**Caching: none.** Unchanged from the original recommendation — the data volumes and traffic this system will see don't justify a caching layer before there's evidence of an actual performance problem.

---

## 9. Summary table

| Layer | Recommendation | Rejected alternatives (why) |
|---|---|---|
| Architecture | Modular monolith, REST API | Microservices (no scaling/team problem to solve), GraphQL (no complex query need) |
| Language | TypeScript (front + back) | Mixed-language stack (adds onboarding cost for a small team) — ⚠ revisit if team already expert elsewhere |
| Backend framework | Fastify | NestJS (too much ceremony for this scope), Express (weaker TS ergonomics) |
| Frontend | React + Vite + TanStack Table/Query | Next.js/Remix (SSR/SEO strengths don't apply to an internal tool) |
| Database | PostgreSQL | (already decided in Database Design v1) |
| ORM | Prisma | Knex (no type safety), TypeORM (weaker tooling/docs) |
| Auth | Self-hosted sessions + argon2 | Auth0/Clerk (unneeded recurring cost/dependency) — ⚠ revisit if SSO/LDAP confirmed needed |
| Background jobs | None initially; BullMQ+Redis if proven necessary | Standing up a queue pre-emptively (unjustified complexity) |
| File storage | Files on disk, path stored in Postgres | Object storage (S3/MinIO — upgrade later if volume ever justifies it) |
| Excel processing | `exceljs`, one consolidated import flow | Custom parsing (solved problem); replicating all 4 legacy import paths |
| Deployment | Docker, single host or small managed platform | Kubernetes (pure overhead at this scale) — ⚠ depends on on-prem vs. cloud decision |
| CI/CD | None — test and deploy by hand | Automated pipeline (add later if deploy frequency or team size make manual steps risky) |
| Testing | Vitest for core business logic, run locally | Playwright/full E2E suite (defer until there's a team/track record to justify it); skipping tests entirely (legacy's approach — the one gap worth fixing regardless) |
| Logging/monitoring | Plain file/stdout logs, checked manually | Sentry, uptime services, full observability stack (all add later only if actually needed) |
| Caching | None | Redis cache (no proven need yet) |

---

## 10. What would change this recommendation

Restating the three open items clearly, since they're the only things that would meaningfully alter this document rather than just adjust a detail:

1. **Team skillset** — if the people building/maintaining this know a different ecosystem well, that should generally override the TypeScript default.
2. **Hosting constraints** — on-prem vs. cloud changes the specific deployment target (§7), not the application architecture itself.
3. **LDAP/SSO requirement** — if confirmed real, changes the auth approach (§5) from self-hosted sessions to an SSO integration.

Everything else in this document — modular monolith, REST, PostgreSQL, no microservices, no premature caching, testing built in from the start — I'd stand behind regardless of how those three questions come back.
