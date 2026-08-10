# Mafali CRM — Migration Roadmap v1

Sequences the effort from Project Estimation v1 into phases. Every phase's effort figures are the same line items from that document, just grouped by dependency rather than by workstream — the totals reconcile exactly (80 / 148 / 241 person-days optimistic/realistic/pessimistic across both documents).

---

## Phase sequence

| Phase | Objective | Depends on | Effort (O/R/P, person-days) | Risk |
|---|---|---|---|---|
| Pre-work — Analysis sign-off | Resolve open questions, confirm schema/architecture direction | This review (done) | 2 / 5 / 10 | Low |
| 0 — Foundation | Deployable skeleton: schema applied, login works, shared UI components exist | Pre-work | 17 / 29 / 46 | Low-Medium |
| 1 — Reference Data | Families, franchises, countries, assistants manageable end-to-end | Phase 0 | 5 / 8 / 12 | Low |
| 2 — Core Client Management | Search, view, and log interactions with clients — the heart of the system | Phase 1 | 15 / 26 / 40 | **High** |
| 3 — History & Filters | Interaction history browsing/search; saved filter builder for both clients and history | Phase 2 | 11 / 21 / 32 | Medium |
| 4 — Revenue & Reminders | Per-client revenue tracking; daily reminder view | Phase 2 | 4 / 8 / 12 | Low |
| 5 — Excel Import/Export | One consolidated bulk import/export feature | Phase 2 | 5 / 9 / 15 | Medium |
| 6 — Data Migration | Legacy data extracted, cleaned, loaded into the new schema | Phase 0 (starts), Phases 2–5 (finishes) | 5 / 10 / 20 | **High** |
| 7 — Testing & UAT | Core logic verified; real staff validate against real (migrated) data | Phases 2–6 | 13 / 25 / 41 | **High** |
| 8 — Training & Cutover | Staff trained, go-live, documentation handed over | Phase 7 sign-off | 3 / 7 / 13 | Medium |
| **Total** | | | **80 / 148 / 241** | |

---

## Phase 1 in detail — Reference Data

This phase covers the four legacy admin screens from Functional Spec Module 5: **families** (Type_Famille), **franchises** (Type_Franchise), **countries** (Pays), and **assistant profiles** (Assistantes). It's deliberately sequenced right after the foundation phase and before any client-facing work, for two reasons: the client form's dropdowns depend on this data existing, and it's the lowest-risk possible first vertical slice — a good way to prove the Phase 0 foundation (schema, auth, shared UI components) actually works end to end before building the one genuinely hard phase (Phase 2) on top of it.

**Build order within the phase, and why:**

1. **Families first.** Simplest possible case — a single name field, list + inline add + delete. This is where the generic "editable reference list" component gets built; every other simple lookup screen in this phase (and potentially elsewhere later) reuses it rather than being built from scratch.
2. **Franchises next.** Structurally identical to families — same component, different table. Should be close to free once families is done.
3. **Countries.** Reuses the same base list component but needs a real detail form on top of it: name, ISO code, dialing code, and phone-format mask, plus a uniqueness check on the name. This was the best-built of the four legacy screens (Fiche_pays), and it's worth matching that — the phone-mask/dialing-code behavior is actively used elsewhere (client and interaction forms apply it when a country is selected).
4. **Assistant profiles last**, and the most distinct of the four: rather than a standalone entity, this is a 1:1 profile extension on top of a `users` row (department, the "can view all saved filters" permission flag, a comment) — so building it depends on picking an existing active user from Phase 0's auth system, not just typing a name. This is also where the Assistantes/GPWUtilisateur duality found during the functional review gets permanently resolved: there's only ever one identity (the user account), with this screen managing the CRM-specific attributes layered on top of it.

**A deliberate small improvement over legacy, worth noting explicitly:** the legacy versions of these four screens had no uniqueness validation at all — nothing stopped two families or two franchises from being created with the same name. The new versions add that validation. It's a trivial addition riding along with work that has to happen anyway, not a separate cost.

**Open item that could add scope here:** the database design left `store` (Magasin_Principal) as free text because no reference table for it existed anywhere in the legacy source, despite it behaving like a category everywhere it's used. If you confirm there's actually a fixed list of stores, a fifth simple lookup screen belongs in this phase — cheap to add now, more awkward to retrofit once client records reference free-text store names.

**Testing scope for this phase:** light, matching the "test what's actually risky" principle from the tech stack decision — the uniqueness checks and the assistant-to-user linkage are the only things here with real logic worth a test; the rest is standard CRUD.

---

## What's strictly sequential vs. what can run in parallel

This is the part that actually matters for calendar time, especially with more than one developer.

**Strictly sequential, no way around it:** Pre-work → Phase 0 → Phase 1 → Phase 2. The schema and auth have to exist before anything else is buildable, and reference data (franchises, families, countries, assistants) has to exist before client screens are meaningfully testable, since they populate half the dropdowns on the client form. Phase 2 itself is also sequential internally — it's the single highest-risk phase and the one place I'd resist compressing by throwing more people at it, since the interaction-workflow logic (Parcours_Client's business rules) is dense enough that splitting it across developers risks inconsistent implementation more than it saves time.

**Genuinely parallel once Phase 2's backend is stable:** Phases 3, 4, and 5 don't depend on each other at all — only on Phase 2 having a working client model. With two developers, this is the natural split point: one continues polishing Phase 2's frontend while the other starts Phase 3 or 4. This is the main reason the two-developer timeline (§3 of the estimation doc, ~17–20 weeks) is meaningfully better than half the solo timeline rather than exactly half — three phases' worth of work becomes parallelizable at once.

**Starts early, finishes late:** Phase 6 (data migration) shouldn't be treated as a single block that happens right before cutover. The profiling and extraction/transformation scripting can — and should — start as soon as the schema is stable (during Phase 0/1), since it's the estimate's biggest unknown and the earlier real data gets examined, the sooner that uncertainty resolves. The actual load-and-validate step happens once Phases 2–5 exist to load data into and check against.

**Depends on everything before it, no shortcuts:** Phase 7 (testing/UAT) needs a staging environment with real migrated data and working versions of every screen — it can't meaningfully start until Phases 2–6 are functionally complete. This is also where the "no legacy test suite" risk lands hardest: there's no faster path through UAT than staff actually using the system, since that's the only validation method available.

---

## Milestones worth treating as go/no-go checkpoints

1. **End of Phase 0** — deployable skeleton exists. Confirms the architecture and stack decisions work in practice before any business logic is built on top of them.
2. **End of Phase 2** — the core client workflow is demoable to actual staff. This is the single most important checkpoint in the whole project: if the "feels familiar" goal is going to fail, this is where you'd see it first, while there's still time to adjust before the remaining phases are built on the same patterns.
3. **Start of Phase 7** — staging environment with real migrated data, all screens functionally complete. The point where the project stops being "trust the plan" and starts being "verify against reality."
4. **End of Phase 7** — UAT sign-off. Go/no-go for cutover.

---

## How the still-open questions affect this specifically

Beyond the general estimate impact already noted, timing matters here: the team-skillset, hosting, and LDAP questions are cheapest to answer **before Phase 0** (they shape the foundation phase directly — redoing auth or deployment setup after Phase 2 is built on top of it is expensive). The data-quality question (real row counts, actual FK integrity) is cheapest to answer **before Phase 6 starts its early scripting work**, ideally during Phase 0/1 while that capacity would otherwise be idle waiting on dependencies. Getting these answered on that schedule, rather than whenever convenient, is the single easiest way to keep the realistic estimate from drifting toward the pessimistic one.
