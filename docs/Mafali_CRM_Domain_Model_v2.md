# Mafali CRM — Domain Model v2

Supersedes v1. Changes below are driven by reading the actual WLangage code and control definitions in Part 3 (windows: Fen_Fichier_Client, Fen_Detail_Client, Fen_Parcours_Client, Fen_Recherche_Client, Fen_Choix_Villes). Everything not mentioned below is unchanged from v1.

## Resolved from v1's open questions

**The three "status" fields on France_Optique now have distinct, confirmed meanings** (found via combo-box content lists, cross-checked against the code that reads/writes them):

- **EtatClient** — client type. Two values: `Client`, `Prospect`. (Internally, the grid-display code in Fen_Fichier_Client maps codes 1→"Client", 2→"Prospet" [sic, typo in the source] — so somewhere a numeric-coded version coexists with the text version; needs a closer look at whether the stored value is the code or the text — flagging as a still-open sub-question.)
- **Status_Vente** — sales pipeline stage. Values: `Devis en cours` (quote in progress), `Vente (BAT Signé)`, `Vente (BAT Original)`, `Vente (Bon de Commande)` (three variants of "sale confirmed," differentiated by which document type was received), `Vente Annulée` (sale cancelled), `Production`. The first four "sale" statuses each require the user to attach a supporting file (quote/order document) via a file picker — stored as a binary blob on the resulting Historique row (see below).
- **Statuts_Clients** — payment/billing status. Values: `Impayés` (unpaid), `Payés` (paid), `Bloqués` (blocked for payment).

These are separate axes that can vary independently: a client can be a Prospect who's mid-quote, or a Client who's unpaid, etc.

**Four franchise fields are a genuine multi-value design, not a legacy artifact.** Franchise, Franchise2, Franchise3, Franchise4 are four independent combo boxes, each bound to the same Type_Franchise reference table. A client can belong to up to four franchises simultaneously. (Still open: why a hard cap of 4 rather than a proper one-to-many table — almost certainly a WinDev-era shortcut. For the new system this should become a real child table, `client_franchise(client_id, franchise_id)`.)

**Cle_Opl (client primary key) is confirmed to be application-managed, not database-managed**, and the mechanism is worse than "just not an auto-identifier" — it's a manual max-search: on creating a new client, the code searches downward from 999,999,999 using `HLitRechercheDernier` until it finds the highest existing Cle_Opl, then uses next value. **This is a genuine concurrency risk**: two users creating a client at the same moment could compute the same "next" key. I'm elevating this to the risk register. Need to know: has this ever caused a collision in production? Worth asking the client-side team, or checking data for irregularities.

**The Assistantes table is likely NOT the live source of "commercial assistant" data.** The Combo_Assistantes dropdown in the main workflow screen (Fen_Parcours_Client) is populated at runtime from a completely different file — `GPWUtilisateur.FIC`, part of the GPW/WDGPU authentication component — not from Assistantes.FIC. It lists every login's first+last name. This means the actual "who can be assigned to a client interaction" list is the user/login directory, and Assistantes.FIC (which has no unique key at all, see v1) may be vestigial, historical, or used only in some other screen I haven't reached yet. **Open question, now more specific: is Assistantes.FIC still written to anywhere, or is it dead data?** I'll confirm when I reach Fen_Fichier_Assistantes / Fen_Detail_Assistantes.

## New findings

**Historique rows are created only through one workflow, never edited in place.** Every "Apply" action in Fen_Parcours_Client always inserts a new Historique row (`HAjoute`) — it's a true append-only interaction log, confirming the denormalized-snapshot design from v1 is intentional: each row captures what was true about the client (name, address, status, franchise) at the moment of that specific interaction. I'm treating this as confirmed intentional, not an artifact — the new system should preserve point-in-time snapshots, likely via an explicit event/audit table rather than relying on incidental duplication.

**Reminder scheduling logic is operation-dependent**, and reveals a real "if no reminder, use a sentinel far-future date" convention: on save, if the reminder date isn't valid, it's set to `30000101` (Jan 1, 3000) rather than left null. Concrete reminder rules by operation:
- `A Rappeler Mois` — prompts for a number of months, sets reminder to today + N months
- `A Rappeler Date` / `A Rappeler Date Heure` — prompts for an explicit date (optionally time)
- `Pas Intéressé` — automatically sets reminder to today + 4 months
- `Vente Annulée` — automatically sets reminder to today + 4 months
- `Vente en Cours` — no automatic reminder; unlocks the Status_Vente combo instead

**Country drives phone number formatting.** Selecting a country (COMBO_Pays, matched against Pays.NomPays) applies that country's `Masque` as the input/display mask for the Telephone and TelBis fields, and populates a separate "dialing code" display field from Pays.Indicatif. Confirms the Pays.Masque field from v1 is an active, used business rule, not dead data.

**Postal-code-to-city autocomplete exists but is inconsistently wired.** In Fen_Parcours_Client it's live: entering a postal code calls an external function `gsRechercherVille()`, auto-fills the city if there's exactly one match, or opens the Fen_Choix_Villes picker if there are several. In Fen_Detail_Client, the equivalent code is present but commented out. **Open question: is Fen_Detail_Client an older/deprecated duplicate of client-editing functionality that's been superseded by Fen_Parcours_Client, or are both actively used for different purposes?** This matters for scoping — no point fully replicating a dead screen. `gsRechercherVille` itself is an external dependency not yet traced to source — likely in the procedure collections (Part 5) or an external component; I'll confirm.

**A "blocked" toggle locks most of the editing surface.** When a client's `bloque` flag is on, the following become disabled: Portable, Fax, the client-update toggle, the note-edit toggle, the CA display group, and the Production combo — only record navigation (Précédent/Suivant/Premier/Dernier) stays active. This is a deliberate workflow safeguard, not just a display flag, and needs to be replicated precisely.

**Editing client master data and editing the note both require an explicit opt-in per session.** Two separate toggle switches ("Inter_MAJ_Client" and "Inter_Modif_Note") must be turned on before their respective fields become editable in the Parcours screen — this prevents a user from paging through client records and accidentally overwriting data. This is a UX/workflow detail directly relevant to the "staff already knows how to use it" goal and should be preserved.

**Hard delete, no confirmation trail.** Deleting a client (Fen_Fichier_Client's Supprimer button) asks "Are you sure?" then calls `TableSupprime`, which removes the underlying record directly — no soft-delete flag, no audit row recording who deleted what or when. Same likely applies elsewhere; will confirm per-window. Flagged in risk register.

**Excel import creates client records unconditionally — no dedup.** The Fen_Fichier_Client "Import" button reads a hardcoded local file (`C:\Documents and Settings\fe\Bureau\Autefage\Fichier France Optique.xls` — note: an old Windows XP-era profile path, this machine/user almost certainly no longer exists) and calls `HCréation` + `HAjoute` for every row, every time, with no check for an existing Cle_Opl. Re-running the same import would create duplicate clients. Column mapping confirmed: Raison_Sociale, Complement, Rue, Localisation_1, Localisation_2, CP, Ville, Telephone, Franchise, Cle_Opl (in that order).

**Help system is a compiled Windows CHM file** (`Aide Autefage005.chm`), referenced from nearly every window's help button. Confirms "Autefage" as the historical product name (still open: relationship between Autefage and Mafali — haven't found an explicit answer in the code yet). This help system has no web equivalent and will need full replacement; not a business-logic concern, just a scope item.

**Search screen (Fen_Recherche_Client) is a genuine multi-criteria search** across ~20 fields (family, company name, all four franchises, address fields, phone numbers, dates, notes, assistant, representative, store, payment status), feeding a dedicated query (Req_Recherche_Partout). Selecting a result and clicking "Phoning" launches the Parcours_Client workflow screen scoped to that result set — confirming the intended pattern: search/filter first, then work the resulting list one record at a time. Filters module (Fen_Filtre) provides a second, saved/named path into the same Parcours_Client workflow (`"FILTRE_CLIENT"` mode) — I'll confirm the distinction once I reach that module.

## Updated risk register (new entries from this pass)

1. **Client ID (Cle_Opl) generation is a manual max+1 scan, not a DB sequence** — real risk of duplicate-key collisions under concurrent record creation. High priority to resolve in new schema design (use a real auto-increment / UUID).
2. **No dedup on Excel import** — repeated imports can create duplicate client records. Need to know how often this import is actually used before deciding whether to fix or faithfully replicate.
3. **Hard delete with no audit trail** on client deletion — conflicts with the stated goal of "improving maintainability" and is worth explicitly deciding to change (soft delete + audit) even though it changes behavior, because current behavior is a data-loss risk, not a deliberate business rule.
4. **Assistantes.FIC vs GPWUtilisateur.FIC discrepancy** — the assistant-name dropdown in the main workflow doesn't use the table I originally modeled as the assistants entity. Domain model for "who can be assigned to a client" needs to be revisited once the auth/user module is reviewed.
5. **`gsRechercherVille` is an untraced external dependency** — need to find its definition (likely Part 5, procedure collections) to know if it's a local reference table, a hardcoded list, or calls an external service — affects whether this feature is portable as-is.

## Addendum (after History, Reminders, Filters, Reference Data, Revenue, Excel modules)

**Assistantes resolved.** Not dead data — it's a legitimate extension/profile table. GPWUtilisateur (the auth component) owns login identity; Assistantes adds CRM-specific attributes (Service/department, an All_Filtres permission-like flag, a comment) for a subset of those logins, with the name always picked from the live login list rather than typed freely (in both its own admin screen and, where active, the Parcours_Client workflow). Model this in the new schema as a profile table with a foreign key to the user/auth entity, not a standalone "assistants" concept.

**EtatClient resolved.** Confirmed stored as a numeric code (1 or 2) on France_Optique, rendered as "Client"/"Prospect" text only at display time (seen identically in two separate windows' grid-row-display code). Model as a small integer or enum in the new schema, not free text.

**Client ID (Cle_Opl) generation is worse than first assessed.** Now confirmed **three** different mechanisms create new client IDs: a manual max+1 scan (Fen_Detail_Client), taking the value straight from an Excel column with no validation (Fen_Fichier_Client's Import), and a user-specified starting number that auto-increments through a batch (Fen_Ajout_Depuis_Excel). None uses a real database sequence. This is now a systemic pattern across the codebase, not an isolated shortcut — reinforces this as a top priority for the new schema design.

**New reference-data behavior:** Type_Franchise entries can be silently auto-created by two of the four Excel import mechanisms when they encounter an unrecognized franchise name — meaning the reference list isn't fully governed by its own admin screen. Worth a deliberate decision in the new system.

**Country/phone handling clarified:** Pays.Masque (phone format mask) and Pays.Indicatif (dialing code) are real, actively-used fields — the country admin screen (Fiche_pays) sources country names from WinDev's built-in ISO country list and auto-suggests the dialing code, rather than these being freely typed. The new system will need an equivalent country/dialing-code reference (e.g. ISO-3166 + E.164 dataset).

**CA (revenue) duplicate risk downgraded.** The Fiche_CA entry screen actively blocks duplicate client+year combinations in application code, even though the database itself doesn't enforce it. Normal usage is protected; only bulk-import or direct-edit paths could introduce duplicates.

## Next
Proceeding to Main window/navigation, then the Auth & user management module (GPW*/WDGPU — the largest remaining piece at 404+ pages), then a quick pass on system/framework windows and the queries/procedures/tests parts.

## Reverse-engineering pass complete

As of this update, all 46 windows, all 8 queries (with full SQL), both procedure collections, the automated-test suite, and the internal WDGPU authentication component have been reviewed — see the Functional Specification for the module-by-module detail and the Risk Register for the consolidated list of findings. The domain model above (client, history, revenue, reference data, filters, plus the auth/permission model described in the Functional Spec Module 9) is now a complete first-pass picture of the business domain, ready to support database design and architecture discussions. Remaining open questions are tracked inline throughout both documents and are not blockers — they're decisions or confirmations to gather from you before finalizing the database design.
