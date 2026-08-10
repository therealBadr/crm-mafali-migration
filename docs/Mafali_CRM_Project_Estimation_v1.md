# Mafali CRM — Project Estimation v1

Figures are in **person-days**, deliberately team-size-independent — divide by however many people are actually building this to get calendar time (a solo developer takes roughly twice as long as two working in parallel, though not exactly, since some work like data migration and UAT doesn't parallelize well). Three estimates per line: optimistic (O), realistic (R), pessimistic (P), plus a confidence level explaining how much weight to put on each number.

---

## 1. First, the good news: 46 legacy windows becomes about 24 real screens

This is worth stating plainly because it directly shrinks the estimate. Of the 46 windows catalogued in the functional spec:

- **10 are standard framework dialogs** (print, "please wait," color/zoom pickers, message boxes) with zero replication cost — a UI component library gives you these for free.
- **1 is confirmed dead code** (Fen_Choix_Mois_Rappel).
- **9 GPW screens + the internal WDGPU component collapse into roughly 3 screens** (login, user administration, role administration) once the legacy's generic multi-application, per-UI-element permission engine is replaced with conventional feature-level RBAC, per the architecture recommendation.
- **4 overlapping Excel import/export screens collapse into 1** consolidated import feature.
- **2 near-identical filter-builder screens (client and history) share one generic component**, built once and reused, rather than two separate implementations.

That leaves **~24 screens** to actually design and build, several of which (the reference-data admin screens — families, franchises, countries, assistants) are simple enough to share one generic "editable reference list" component rather than being built individually.

---

## 2. Effort by workstream

| Workstream | Optimistic | Realistic | Pessimistic | Confidence | Notes |
|---|---|---|---|---|---|
| Analysis & sign-off | 2 | 5 | 10 | **High** | Bulk of this is already done via the functional spec/domain model/schema; remaining is your answers to open questions and final sign-off |
| Architecture & project setup | 3 | 5 | 8 | **High** | Scaffolding only — no CI/CD, no cloud infra setup, per the simplified stack |
| Database implementation | 2 | 4 | 6 | **High** | Schema design is done; this is running migrations and seeding lookup tables |
| **Data migration (legacy → new)** | 5 | 10 | 20 | **Low** | The single biggest unknown in this whole estimate — see §4 |
| Auth & permissions | 5 | 8 | 12 | **Medium-High** | Simplified vs. legacy; exact scope depends on the still-open RBAC granularity question |
| Backend — client + search/filter | 4 | 7 | 10 | Medium | |
| Backend — interaction workflow (Parcours_Client logic) | 5 | 9 | 14 | Medium | The single most business-logic-dense piece of the whole system |
| Backend — history + filter builder | 3 | 6 | 9 | Medium | |
| Backend — revenue | 1 | 2 | 3 | High | Simple by comparison |
| Backend — reference data | 2 | 3 | 5 | High | |
| Backend — saved filters (consolidated) | 2 | 4 | 6 | Medium | |
| Backend — Excel import/export (consolidated) | 3 | 6 | 10 | Medium | |
| Backend — reminders | 1 | 2 | 3 | Medium | Pending confirmation the underlying logic (RappelRDV flag) is even meaningful |
| Frontend — client screens (list, detail, Parcours_Client) | 6 | 10 | 16 | Medium | Highest-value screen for "feels familiar" — worth the investment |
| Frontend — history screens | 3 | 5 | 8 | Medium | |
| Frontend — filters (builder + apply + admin) | 3 | 6 | 9 | Medium | |
| Frontend — reference data admin (shared component) | 3 | 5 | 7 | High | |
| Frontend — revenue | 1 | 2 | 3 | High | |
| Frontend — reminders | 1 | 2 | 3 | High | |
| Frontend — Excel import/export | 2 | 3 | 5 | Medium | |
| Frontend — auth screens | 3 | 5 | 8 | High | |
| Frontend — shared components (grid, forms, layout) | 3 | 5 | 8 | Medium | Built once, amortized across every screen above |
| Testing (core business logic, run locally) | 5 | 10 | 16 | Medium | Deliberately scoped to logic that's shown itself risky, not exhaustive coverage |
| UAT & bug-fixing | 8 | 15 | 25 | **Low** | No legacy test suite to validate against — real user testing is the only safety net, see §4 |
| Deployment setup | 1 | 2 | 4 | High | Simple Docker deploy, no orchestration |
| Training / cutover support | 2 | 4 | 8 | Medium | Should be light if the UX genuinely feels familiar — a real test of whether the project succeeded |
| User-facing documentation | 1 | 3 | 5 | High | Quick-reference material, not the technical docs already produced |
| **Total** | **80** | **148** | **241** | | ≈ 16 / 30 / 48 person-weeks |

---

## 3. What a calendar timeline looks like

Person-days aren't calendar time — that depends on headcount. Using the realistic estimate (148 person-days):

- **One developer, full-time:** ~30 weeks (~7 calendar months). Nothing parallelizes; everything is sequential.
- **Two developers, full-time:** meaningfully faster than half, but not exactly half — data migration and UAT don't split well across two people, and some early sequencing (schema and auth have to exist before most feature work can start) limits how much can run in parallel. A realistic range is **17–20 calendar weeks (~4–5 months)**.

I'd treat the two-developer scenario as the more useful planning number if there's any flexibility on headcount — the sequential dependencies (schema → auth → everything else) mean a single developer pays for that dependency chain in full, with no way to work around it.

---

## 4. The two things most likely to blow this estimate up

**Data migration (Low confidence, 5/10/20 range).** This estimate is essentially a placeholder until real production data is examined. The risk register flagged several data-quality unknowns that directly determine migration effort: whether Cle_Opl collisions actually exist in the live data, how many orphaned foreign-key-equivalent references exist (client names that don't match any Assistantes/Type_Franchise/Pays row), whether duplicate CA rows exist from historical imports, and how EtatClient's coded-vs-text inconsistency actually manifests in stored data. **Recommend getting read access to the live legacy database (or a recent backup) before finalizing this number** — even a few hours of profiling the actual data would tighten this range substantially.

**UAT & bug-fixing (Low confidence, 8/15/25 range).** With zero legacy automated tests, there's no executable specification to check "does the new system behave the same" against — every behavior in the functional spec was reverse-engineered from source code, not verified against expected outputs. That makes real staff usage during UAT the primary way discrepancies get caught, and how smoothly that goes depends entirely on staff availability and how many of the "open questions" (which screens are actually used, whether the reminder popup works as expected today, etc.) turn out to have surprising answers.

---

## 5. What would tighten these numbers

In order of impact: sample of real production data (addresses §4's biggest unknown directly), confirmed team size/headcount (turns the person-day total into an actual calendar commitment), and answers to the three open questions from the tech stack document (LDAP, hosting, and — new here — how many of the four Excel import mechanisms are actually in regular use, since that affects the import consolidation estimate specifically).
