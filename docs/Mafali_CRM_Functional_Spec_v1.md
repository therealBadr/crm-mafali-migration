# Mafali CRM — Functional Specification v1

Module 1 of N: **Client Management**. Source: Part 3 windows Fen_Fichier_Client, Fen_Detail_Client, Fen_Parcours_Client, Fen_Recherche_Client, Fen_Choix_Villes.

---

## Screen: Fen_Fichier_Client — "Table des Clients" (Client List)

**Purpose.** The client master list/grid — entry point for browsing, creating, editing, deleting, and importing clients.

**Actors.** Any logged-in user (no role restriction observed yet in this window — permissions may be enforced elsewhere, e.g. GPW groups; to be confirmed in the auth module).

**Layout.** A single grid bound directly to France_Optique, showing ~30 columns (Famille, Raison_Sociale, Complement, Franchise ×4, Rue, Localisation 1/2, CP, Ville, Pays, Telephone, Portable, Fax, Email, Cle_Opl, Assistante_Commerciale, Magasin_Principal, Statut_Client, Représentant, Responsable_Achat, Opération, Status_Vente, Date/Heure_Saisie, Date/Heure_Rappel, NotePer, EtatClient). Record count label at bottom.

**Actions / buttons.**
- **Nouveau** — resets a working query (Req_Main), opens Fen_Detail_Client in creation mode.
- **Modifier** — requires a row selected; opens Fen_Detail_Client in edit mode for that record.
- **Supprimer** — requires a row selected; confirms via Yes/No dialog ("Are you sure you want to delete this client?"); on confirm, deletes the row directly from the underlying table. **No soft delete, no audit record.**
- **Import** — reads a hardcoded local Excel file path and bulk-inserts new client records (see Data rules below).
- **Fermer** — closes the window.
- Help (icon) — opens page 14 of the compiled help file.

**Display rule.** EtatClient is rendered as "Client" or "Prospect" in the grid based on an underlying code value (1/2) — confirms EtatClient has both a coded and a display form; needs verification of which is actually persisted.

**Data rules.**
- Import maps Excel columns 1–10 to Raison_Sociale, Complement, Rue, Localisation_1, Localisation_2, CP, Ville, Telephone, Franchise, Cle_Opl (with spaces stripped from the Cle_Opl value). Every row becomes a new record — **no matching against existing Cle_Opl, so re-import duplicates data.**
- On window load, a cleanup routine scans every France_Optique record and strips non-breaking-space characters (char code 160) out of Cle_Opl, rewriting the field. Runs as a full table scan on every open — a legacy workaround, likely for historical data corruption; not something to replicate literally, but worth asking whether it's still needed (i.e., is the source of the corruption still active anywhere?).

**Open questions.**
- Is any permission/role check applied to Nouveau/Modifier/Supprimer, or is this uniformly available to all logged-in users?
- Is the Excel import feature still actively used, or legacy/rarely-touched? Affects whether we replicate faithfully or redesign with proper upsert semantics.

---

## Screen: Fen_Detail_Client — Client detail/edit form (simple)

**Purpose.** A contact-info edit form for a single client — narrower in scope than Fen_Parcours_Client (see below). Opened from Fen_Fichier_Client's Nouveau/Modifier.

**Fields editable:** Raison_Sociale, Complement, Rue, Localisation_1/2, Code_Postal, Ville, Pays, Telephone, Portable (Sai_Portable), Fax, Email, Responsable_Achat, Représentant.

**Fields read-only (grayed) in this screen:** Cle_Opl (correct — it's the PK), Assistante_Commercial (both instances), Magasin, Note, Statut_Client, Date_Saisie, Date_Rappel, Heure_Saisie, Heure_Rappel. **This means status, notes, reminders, and assistant assignment cannot be changed here at all** — that logic lives entirely in Fen_Parcours_Client.

**Validation rules.**
- Email: must match `[-.a-zA-Z0-9]+[@][-.a-z0-9]+[.][a-z]{2,5}` on field exit, or a blocking error is shown and focus returns to the field.
- Reminder date: if invalid on save, silently reset to sentinel value 3000-01-01 rather than left blank/null.
- Postal code → city autocomplete exists in this screen's code but is **commented out / disabled** — currently no effect.

**Save behavior.** Works against a query alias (Req_Main), not France_Optique directly. New records use `HAjoute`, existing use `HModifie`. Window title bar displays the current logged-in user's name and group, sourced from the GPW auth module.

**Open question.** Given Fen_Parcours_Client covers everything this screen does plus far more (status, reminders, notes, history logging), is Fen_Detail_Client still reached in normal daily use, or mainly a legacy/edge-case path (e.g., quick add of a brand-new client before working them into the pipeline)?

---

## Screen: Fen_Parcours_Client — "Parcours Client" (Client Journey / Working Screen)

**This is the primary, business-critical screen of the CRM** — where staff actually work through a list of clients (from a search or saved filter) one at a time, logging interactions, updating sales/payment status, scheduling follow-ups, and attaching documents.

**Purpose.** Sequential "work the call list" tool: navigate Précédent/Premier/Suivant/Dernier through a result set (produced by either Fen_Recherche_Client's search or Fen_Filtre's saved filter — indicated by a `gsFenetre` parameter of `"RECH_CLIENT"` or `"FILTRE_CLIENT"`), and for each client, record the outcome of that day's interaction.

**Actors.** Sales/commercial assistants (Assistante Commerciale).

### Layout & controls
- Client identity/address fields (mostly editable only when the "update client" toggle is on — see below)
- **COMBO_Pays** — country selector; on change, clears phone fields and re-applies the country's phone mask (from Pays.Masque) and dialing code (Pays.Indicatif) to Telephone and TelBis.
- **COMBO_Etat_Client** — Client / Prospect.
- **Combo_Franchise / 2 / 3 / 4** — up to four franchise memberships, each a dropdown over Type_Franchise.
- **Combo_Assistantes** — the assigned commercial assistant. Populated at runtime from the GPW login directory (GPWUtilisateur.FIC), not from the Assistantes table. Defaults to the current logged-in user, but can be reassigned. **Required** — attempting to save without a selection blocks with a warning.
- **Combo_Operation** — the action being logged this interaction: `A Rappeler Mois`, `A Rappeler Date`, `A Rappeler Date Heure`, `Pas Intéressé`, `Vente en Cours`, `Production`.
- **Combo_Status** ("Status de la Vente") — only active when Operation = `Vente en Cours`: `Devis en cours`, `Vente (BAT Signé)`, `Vente (BAT Original)`, `Vente (Bon de Commande)`, `Vente Annulée`, `Production`.
- **Combo_Statuts_Clients** ("Statut Client" — payment status): `Impayés`, `Payés`, `Bloqués`.
- **COMBO_Production** — visible only when Operation or Status = "Production"; a sub-list of production stages: En test, Test couleur valide date, A imprimer, imprimé, Calandré, Conditionné, Facture, Livré. (This list strongly suggests Mafali's business involves a print/manufacturing production process, not just point-of-sale — worth confirming with you what "Mafali Optique" actually produces.)
- **INT_Bloqué** (toggle) — "blocked" flag; see business rule below.
- **Inter_MAJ_Client** (toggle) — must be explicitly switched on to unlock client-detail editing.
- **Inter_Modif_Note** (toggle) — must be explicitly switched on to unlock the Note field.
- **Table_Histo** — this client's interaction history (read from Historique via a parameterized query).
- **LISTE_CA** — this client's recent revenue figures (via REQ_CA_Derniers).
- Navigation counter ("N / Total") showing position within the working set.

### Business rules

**Reminder scheduling by operation:**
| Operation | Resulting Date_Rappel |
|---|---|
| A Rappeler Mois | today + user-entered N months |
| A Rappeler Date | user-entered date |
| A Rappeler Date Heure | user-entered date + time |
| Pas Intéressé | today + 4 months (automatic) |
| Vente en Cours | none automatic; unlocks Status combo |
| Production | n/a |

**Status-triggered file attachment:** selecting `Devis en cours`, `Vente (BAT Signé)`, `Vente (BAT Original)`, or `Vente (Bon de Commande)` opens a file picker; the selected file is attached as a binary memo to the new Historique row (field FicStk). If the user cancels file selection, the status selection is reverted.

**Vente Annulée** also auto-sets the reminder to +4 months.

**Blocked-client lockout:** when INT_Bloqué is on, these become disabled: Portable, Fax, both edit-toggles, the CA display group, and the Production combo. Only record navigation remains active. A "blocked" label becomes visible.

**Postal code → city:** entering a postal code with exactly one matching city auto-fills Ville and sets Pays to "FRANCE"; multiple matches open the Fen_Choix_Villes picker; no matches leave the fields for manual entry. (External dependency `gsRechercherVille` — source not yet located.)

**Save ("Appliquer") sequence:**
1. Require an assistant selected (blocking).
2. Confirm via Yes/No dialog.
3. Stamp Date_Saisie/Heure_Saisie = now.
4. Re-fetch the client record by Cle_Opl (defends against staleness from paging).
5. Update France_Optique: Note (only if note-edit toggle on), Production (only if visible) — saved via HModifie.
6. Build and insert (always `HAjoute`, never update) a new Historique row capturing: client ref, timestamp, assistant, reminder date/time, operation-derived text, status (if "Vente en Cours"), note, franchise, city, postal code, store, client payment status, and any attached file.
7. Clear the Note field.
8. Auto-advance to the next client in the working set.

**History attachment viewer:** selecting a history row and clicking the "view attachment" button extracts the stored binary memo to a temp file (named `01` + original extension) and launches it with the OS's default associated application. Shows "no attachment" if none exists.

**Revenue detail:** a button opens Fiche_CA for the current client (full CA breakdown — window not yet analyzed in detail).

**Search re-entry guard:** attempting to reopen Fen_Recherche_Client while it's already open shows "this window is already open" rather than opening a second instance.

### Open questions
- Does INT_Bloqué (blocked) have a specific business meaning distinct from Statuts_Clients = "Bloqués" (payment-blocked)? They appear to be two different concepts (a boolean flag vs. a payment-status enum value) that might overlap in intent — worth clarifying so the new system doesn't conflate them.
- What determines the difference between the coded (1/2) and text ("Client"/"Prospect") forms of EtatClient seen in different windows?
- Confirm what "Production" actually means for this business — is Mafali Optique manufacturing something (signage, lenses, printed materials)? The production-stage list (calandré/conditionné/imprimé) reads like a print-production pipeline.

---

## Screen: Fen_Recherche_Client — "Recherche Client Multi-Critères"

**Purpose.** Multi-field search form producing a result list that feeds Fen_Parcours_Client.

**Search fields (~20, all optional/combinable):** Famille, Raison_Sociale, Complement, Franchise ×4, Rue, Localisation 1/2, Code_Postal, Ville, Pays, Cle_Opl, Telephone, Telephone1 (bis), Portable, Fax, Email, Assistante_Commerciale, Responsable_Achat, Date_Rappel, Heure_Rappel, Date_Saisie, Heure_Saisie, Note, Combo_Operation (defaults "Toutes"/All), Combo_Status (defaults "Tous"/All), Représentant, Magasin_Principal, Statuts_Clients.

**Buttons.**
- **Btn_Rechercher** — runs the search (Req_Recherche_Partout) with whatever criteria are filled in; blank fields are excluded from the filter.
- **Btn_Effacer_Tout** — clears all criteria and re-runs an unfiltered query.
- **Btn_Charger_Client** — loads the selected result row back into the search form fields.
- **Btn_Appliquer_Modification** — requires a result selected; writes the search-form field values back onto the underlying query row (`HModifie(Req_Recherche_Partout)`) — i.e., this search screen can itself edit records, not just find them.
- **BTN_Phoning** — requires a result row selected; opens Fen_Parcours_Client scoped to the full result set, starting the "work the list" workflow, in `"RECH_CLIENT"` mode.
- Custom title-bar buttons (minimize/maximize/close) — this window has no native title bar; a superchamp component (SC_BoutonsSystème) simulates one. Notably, its close button is blocked with a message ("not allowed — Fen_Parcours_Client depends on this window") whenever this search window is the active source for an open Parcours session — **you cannot close the search screen while its result set is being worked**, presumably because Parcours_Client reads live from this window's result table rather than an independent copy.

**Open question.** The "Btn_Appliquer_Modification" behavior (editing a client directly from the search screen) seems to overlap with Fen_Parcours_Client's editing capability — is this a secondary, rarely used edit path, or a commonly used shortcut? Matters for how much of it needs replicating vs. simplifying.

---

## Screen: Fen_Choix_Villes — City picker (popup)

**Purpose.** Simple modal popup listing multiple candidate cities matching a postal code (from the `gsRechercherVille` lookup), for the user to disambiguate. Double-click or Valider (with a selection) returns the chosen city name to the caller; Annuler returns nothing. Positioned adjacent to the postal code field that triggered it.

---

## Cross-cutting observations for this module

- Every window's title bar displays "[FirstName LastName (Group)]" from the current session — confirms a session/identity concept exists globally (from the GPW auth module) and that "Group" (role) is tracked per user, relevant for RBAC design.
- Every window's help button opens the same compiled CHM file at a window-specific page number — a systematic help system, not ad hoc; will need a full replacement (e.g., in-app contextual help) if preserving this is in scope.
- Comments in the code reference a ticket system ("GLPI 3030") for a 2014 fix — confirms this system has had ongoing maintenance/bug-fix history worth being aware of when deciding which behaviors are "intentional business rules" vs. "known bugs that got patched over."

---

## Module 2: History

### Screen: Fen_Fichier_Historique — Raw history grid

**Purpose.** Direct browse/CRUD grid over the Historique table, independent of the Parcours_Client workflow.

**Filtering.** A dropdown (Combo_Filtre_Historique) applies a saved, named filter (from Filtre_Historique) to the grid.

**Actions.** Nouveau / Modifier / Supprimer, all standard — **and this contradicts the "Historique is append-only" assumption from the Client module.** History rows can be freely created, edited, or hard-deleted here, same "Are you sure?" pattern, no soft delete, no audit of who changed what. This is a real governance gap if Historique is meant to be a reliable interaction audit trail: today, anyone with access to this screen can rewrite history.

### Screen: Fen_Detail_Historique — History record edit form

**Purpose.** Generic add/edit form for a single Historique row.

**Fields, all freely editable, no validation found:** Num_Client (numeric — meaning a history entry could technically be re-pointed to a different client with no check), Date_Saisie, Heure_Saisie, Date_Rappel, Heure_Rappel, Assistante_Commerciale, Opération, Status_Vente, Note, Franchise, Raison_Sociale, CP, Ville.

**Save.** Plain Add-or-Modify, no business logic, no field-level validation of any kind — this is a bare WinDev auto-generated CRUD form with no customization. Confirms this really is an unrestricted admin path onto the audit table.

### Screen: Fen_Recherche_Historique — Dynamic filter builder for History

**Purpose.** Lets a user build an arbitrary multi-condition filter across **every field in the Historique table** (enumerated dynamically via `HListeRubrique`, excluding binary/memo fields), two-step: (1) build the query visually by picking, per field: condition type (Equal, Not equal, Greater than, Greater-or-equal, Less than, Less-or-equal, Between, Starts with, Doesn't start with, Contains, Doesn't contain) plus one or two comparison values; (2) click Rechercher to compile these into an HFSQL filter expression and show matching rows in a results table (with a live count).

**Save as named filter.** The compiled condition can be saved as a new row in Filtre_Historique — capped at 512 characters (matches the schema's Filtre_Réel field size exactly); oversized filters are rejected with an error asking the user to simplify.

**This is a materially different feature than it first appears** — not a few canned filters, but a full generic query builder against the entire table schema. This is a significant feature to size correctly for the new system (a generic per-field filter UI, not a handful of hardcoded search fields).

---

## Module 3: Reminders

### Screen: FEN_Rappel — Due reminders list

**Purpose.** A report-style grid (read-mostly) showing reminders, driven by a parameterized query (REQ_RappelRDV) taking an assistant and a date as parameters — i.e., "reminders due for assistant X as of date Y." Only a Close button; no custom code beyond that. The real logic lives in the query definition (Part 4, not yet reviewed) and in whatever launches this window (likely Fen_Principale — not yet reviewed).

### Screen: FEN_Rappel_Client — Reminder quick-view popup

**Purpose.** A small, read-only popup (all fields display-only) showing a client's family/category, company name, phone, mobile, and reminder date/time. Only a Close button. Presumably opened from FEN_Rappel when a row is selected, though the triggering code wasn't found in this window's own script — likely wired from the caller.

### Screen: Fen_Choix_Mois_Rappel — Month-offset picker

**Purpose.** A 3-option radio selector (values map to +4, +6, or +12 months) intended to compute a reminder date. **Finding: this popup appears to be dead code.** The only place it's called from (a `"Vente"` case in Fen_Recherche_Historique's commented-out legacy code) is commented out. No live code path currently opens this window. Flagging as a likely obsolete feature — worth confirming with you before deciding whether to carry it into the new system.

### Screen: Fen_Update — Excel-based bulk upsert (distinct from the "Import" button in the Client module)

**Purpose.** A proper bulk update/insert tool for France_Optique from an Excel file (.xlsx/.xls), with a progress bar and duration timer. **This is meaningfully better-designed than Fen_Fichier_Client's "Import" button**: it matches each Excel row against an existing client by Cle_Opl (column 11 in the sheet); if found, it compares field-by-field and only writes changed fields (Raison_Sociale, Complement, Rue, Localisation_1/2, CP, Ville, Telephone, Franchise) via HModifie; if not found, it inserts a new record via HAjoute. Rows with a missing/non-numeric client ID are skipped and counted separately. Reports a final summary: records added, records modified, records skipped for missing ID, and total duration.

**Duplicated functionality flag.** There are now three distinct Excel-related import/update mechanisms identified so far: Fen_Fichier_Client's naive "Import" (always-insert, no dedup), this Fen_Update (proper upsert by Cle_Opl), and FEN_Modification_Depuis_Excel (not yet reviewed — task pending). This looks like functionality that evolved over time without the older version being removed. **Open question for you: which of these is actually used in day-to-day operation?** This matters a lot for scoping the new system's import feature — I'd design around whichever is real, not replicate all three.

---

## Module 4: Filters

Two parallel filter subsystems exist — one for clients (France_Optique), one for history (Historique) — structurally identical, confirming a consistent internal pattern rather than two unrelated features.

### Screen: Fen_Definir_Filtre / Fen_Recherche_Historique — Filter builder (client / history)

Already described under Module 2 for the history variant; the client variant (Fen_Definir_Filtre) is byte-for-byte the same logic, operating on France_Optique instead. Saves to Filtre_Opératrice instead of Filtre_Historique, and additionally requires selecting an "operator" (Combo_Assistantes, required, tied to Filtre_Opératrice.Nom_Operateur) — confirming saved client filters are personal/per-user, while saved history filters (no operator field on Filtre_Historique) are global/shared across all users. This asymmetry is a real, intentional design difference, not an oversight.

Underlying condition vocabulary (identical for both): Equal, Not equal, Greater than, Greater-or-equal, Less than, Less-or-equal, Between, Starts with, Doesn't start with, Contains, Doesn't contain — combined with AND between fields (multi-condition filters are always AND'd together; there's no OR-between-fields option in the builder itself, only implicitly via the "Between"/"contains" style operators per field).

### Screen: Fen_Filtre — Apply a saved client filter

**Purpose.** Pick a previously saved filter by name (Combo_Filtre_Opératrice, sourced from Filtre_Opératrice.Nom_Filtre) and apply it to the client list. Shows a live count ("N Client(s) pour le filtre sélectionné"). A "Parcourir" button opens Fen_Parcours_Client scoped to the filtered set (`"FILTRE_CLIENT"` mode) — this is the second of the two entry points into the main workflow screen (the other being Fen_Recherche_Client's search results).

**Guard.** This window refuses to close while Fen_Parcours_Client is actively browsing its result set (same pattern as Fen_Recherche_Client) — "not allowed, Parcours_Client depends on this window."

**Also exposes direct in-place editing** of the client grid via a toggle (INT_Modifier_Enregistrement) that switches the underlying table into edit mode — a fourth path to editing client data, alongside Fen_Detail_Client, Fen_Parcours_Client, and Fen_Recherche_Client's "apply modification."

**Confirms EtatClient is stored as a numeric code** (1 or 2), displayed as text ("Client"/"Prospect") only at render time — resolves the open question from Module 1.

### Screens: Fen_Fichier_Filtre / Fen_Detail_Filtre (client), Fen_Filtre_Historique / Fen_Detail_Filtre_Historique (history)

**Purpose.** Plain admin CRUD screens for managing the saved filters themselves (list, create, edit, delete) — separate from the filter-builder screens that generate the underlying condition text. Fen_Detail_Filtre requires a non-empty, and implicitly-expected-unique (not code-validated) filter name, and an operator selection; Fen_Detail_Filtre_Historique has no operator field, consistent with the global/shared design noted above.

**Minor defects found (worth flagging, not fixing):** the delete-confirmation dialogs in both Fen_Fichier_Filtre and Fen_Filtre_Historique contain copy-pasted text referring to the wrong entity — one asks "Are you sure you want to delete this **country**?" and the other "...this **client**?" — both should say "this filter." Harmless, but a useful signal of where copy-paste-without-review happened in this codebase; worth a quick scan for similar leftover text elsewhere.

### Open questions carried forward
- Given four distinct paths to edit client data (Detail_Client, Parcours_Client, Fen_Recherche_Client's apply-modification, Fen_Filtre's in-place edit), which are actually used regularly? This significantly affects how much of the client-editing UX needs to be replicated versus consolidated.
- Confirm whether saved-filter names need to be unique per operator (the schema's composite key suggests so, but the save code doesn't explicitly check before insert — worth checking actual production data for existing duplicates).

---

## Module 5: Reference / Admin Data

### Screen: Fen_Fichier_Assistantes / Fen_Detail_Assistantes — Assistant profiles

**This resolves the open question from Module 1.** Assistantes.FIC is not dead or disconnected — it's a genuine extension table. Its detail form's name field (Combo_Prenom_Nom) is populated from the same live login directory (GPWUtilisateur.FIC) used everywhere else, so a "commercial assistant" profile is always tied to a real system login, not free text. What this table actually adds on top of the login identity is: **Service** (department), **All_Filtres** (a permission-like flag — access to all saved filters, presumably bypassing the per-operator restriction seen in Module 4), and **Commentaire** (free note). So the accurate model is: GPWUtilisateur/WDGPU owns authentication identity; Assistantes is a CRM-specific profile/permissions extension for a subset of those logins. Standard admin CRUD (Nouveau/Modifier/Supprimer), same delete-confirmation pattern.

**Open question:** what does All_Filtres actually gate in practice? I've only seen Filtre_Opératrice filters scoped by operator name so far (Module 4) — need to find where this flag is actually checked (likely in Fen_Filtre's combo population, which I have not yet inspected for that detail).

### Screens: Fen_Fichier_Famille, Fen_Fichier_Franchise — Simple lookup-table admin

**Purpose.** Manage the Type_Famille and Type_Franchise dropdown source lists. Both are minimal: no Nouveau/Modifier buttons at all — only Fermer and Supprimer alongside a directly inline-editable grid (new rows are added by typing into a blank row in the grid itself, standard WinDev inline-edit behavior, not a separate popup). Single free-text field each (Nom_Famille, Nom_Franchise), no validation, no uniqueness check in code.

**Notable: these franchise/family reference lists can also grow silently through Excel import** (see Module 7) via an auto-provisioning helper — so the "official" list of franchises isn't fully controlled through this admin screen alone.

### Screens: FEN_ListePays / Fiche_pays — Country admin (the best-built reference screen)

**Purpose.** Manage the Pays table. Noticeably more carefully built than the Famille/Franchise screens: real Nouveau/Modifier/Supprimer with a proper detail popup (Fiche_pays).

**Business rules:**
- Country name must be chosen from WinDev's **built-in country list** (`PaysListe()`) rather than typed freely — ensures standardized naming.
- Dialing code (Indicatif) auto-suggests from the selected country via a built-in lookup (`PaysIndicatif(PaysCodeIso(...))`), but remains editable and is required.
- Phone mask (Masque) is required, free text — this is the format pattern applied to phone fields elsewhere (Module 1) when this country is selected.
- Duplicate country names are explicitly blocked before insert/update ("Ce pays existe déjà dans votre table").

**Architecture note.** WinDev ships a built-in ISO country reference (name/dialing-code lookups) that this feature leans on — the new system will need an equivalent (e.g., a bundled ISO-3166/E.164 country+dialing-code dataset or a library like libphonenumber) rather than reinventing this from scratch.

---

## Module 6: Revenue (CA)

### Screen: Fiche_CA — Per-client revenue by year

**Purpose.** Opened from Fen_Parcours_Client for the current client; shows and edits that client's CA (revenue) rows in an editable grid (one row per year), sourced from REQ_Tout_CA.

**Actions.** Nouveau (adds a blank row for inline entry), Supprimer (confirms, then hard-deletes), Valider/Annuler at the row level via grid edit.

**Validation rules:**
- Year (SAI_Année) required, must be exactly 4 characters.
- Revenue amount (SAI_CA) required.
- **Client+year uniqueness is actively enforced here in application code** — before saving, it checks for an existing CA row with the same client and year (using the same composite key the schema defines as non-unique/Doublon) and blocks with "please enter a different year" if one exists. **This meaningfully de-risks Risk #6 from the register**: normal usage through this screen cannot create duplicates; the schema-level gap only matters for data entered through other paths (direct import, manual DB edits, or bugs elsewhere). I'm downgrading that risk's probability accordingly, still worth a real DB constraint in the new schema as defense in depth.

---

## Module 7: Excel Import/Export — four overlapping mechanisms

Documenting all four found so far side by side, since they overlap significantly and this is a strong candidate for consolidation in the new system rather than faithful replication of all four.

| Screen | Direction | Match key | Behavior on match | Behavior on no match | Columns | Notes |
|---|---|---|---|---|---|---|
| Fen_Fichier_Client "Import" button | Insert only | none (doesn't check) | n/a | Always inserts | 10 | Hardcoded file path (stale, XP-era); the crudest of the four |
| Fen_Update | Upsert | Cle_Opl (col 11) | Updates only changed fields | Inserts new | 10 | Cleanest logic; reports Added/Modified/Missing-ID counts + duration |
| Fen_Ajout_Depuis_Excel | Insert only | none | n/a | Always inserts; **auto-generates Cle_Opl** sequentially from a user-entered starting number | ~24 | User also picks which Excel sheet to read; formats Fax as XX.XX.XX.XX; auto-creates new Type_Franchise rows for unrecognized franchise values |
| FEN_Modification_Depuis_Excel | Update only | Cle_Opl (col 15, required in file) | Updates only non-blank cells; **a cell value of "#" explicitly clears that field**, blank means "leave unchanged" | Silently does nothing, no count reported | ~24 | The "#"-to-clear / blank-to-skip convention is a genuinely good pattern worth keeping conceptually; also auto-creates Type_Franchise rows |

**Cross-cutting risk (elevates Risk #1 from the register):** three different mechanisms generate new Cle_Opl values three different ways — a manual max+1 scan (Detail_Client), directly from an Excel column (Fichier_Client import), or a user-specified starting number that auto-increments (Ajout_Depuis_Excel). None uses a real database sequence. This is now a confirmed, systemic pattern, not a one-off.

**New business rule found:** unrecognized franchise names encountered during either bulk-insert or bulk-update Excel imports are **silently auto-added** to the Type_Franchise reference table (via a shared helper `Ajoute_TypeFranchise_Si_Non_Paramètré`) rather than rejected or flagged. This means the "canonical" franchise list isn't fully controlled by the admin screen (Module 5) — it can grow unattended through imports. Worth deciding deliberately for the new system: auto-provision (current behavior) vs. validate-and-reject.

**Open question for you:** of these four, which one(s) do staff actually use today? I'd like to design the new system's import feature around real usage rather than replicating all four independently.

---

## Module 8: Main Window / Navigation (Fen_Principale)

### Full menu map (this is effectively the application's navigation structure)

- **Fichier** → Quitter (exit)
- **Paramètres** (admin/reference data) → Fichier des Clients, Fichier des Historiques, Fichier des Familles, Fichier des Franchises, Fichier des Assistantes, Fichier des Filtres Clients, Fichier des Filtres Historiques, Fichier des pays — one entry per reference/admin screen already documented in Modules 2, 4, and 5.
- **Outils** (tools) → Définir un Filtre (submenu: Client, Historique), Initialisation Date Rappel, Mise à jour du fichier France Optique, Ajouter des enregistrements depuis un fichier Excel, Modifier des enregistrements depuis un fichier Excel, Réindexer la base.
- Two large buttons on the home screen itself: "Open Selection" (→ Fen_Filtre, saved-filter picker) and a second one (→ Fen_Recherche_Client, multi-criteria search) — these are the two real entry points into daily work, consistent with the "search or filter, then work the list in Parcours_Client" pattern established in Module 1.

### New findings

**Startup reminder popup.** On launch, the app automatically runs REQ_RappelRDV parameterized by the current user's name and today's date, collects any due reminders that have a specific time set (Heure_Rappel not blank), and opens FEN_Rappel as a child window showing them — a "here's what you need to call back today" greeting. This is the trigger I couldn't find earlier in Module 3.

**First confirmed RBAC rule.** There is a user group called "SUPERVISEUR" with elevated access. When a non-supervisor clicks the saved-filter entry point (Btn_Ouvrir_Selection): the system looks up the user's Assistantes profile by name match; if not found, the feature is blocked entirely with an error; if found, the All_Filtres flag on that profile determines whether they see every saved client filter or only their own (their name is passed as a filter parameter to the underlying query). Supervisors always see everything, unconditionally. **This is the first concrete evidence of a role/permission system affecting behavior**, and confirms Assistantes.All_Filtres (Module 5) is a real, active gate, not vestigial. I expect more RBAC detail once the GPW/WDGPU module is reviewed — that's almost certainly where "SUPERVISEUR" and other groups are defined and where login-time role assignment happens.

**"Initialisation Date Rappel" (bulk maintenance tool).** Scans every France_Optique record and sets Date_Rappel to the sentinel 3000-01-01 wherever it's currently blank/invalid — confirms the sentinel-date convention (seen earlier in the save logic) is a deliberate, systemic design choice reinforced by an admin cleanup tool, not just an incidental save-time default.

**"Réindexer la base" (reindex database).** Runs HFSQL's index-repair function on Historique and France_Optique, then releases transaction locks. This is legacy file-based-database maintenance with no equivalent need in a modern RDBMS (which handles indexing/locking automatically) — not something to replicate, but its presence as a menu item suggests index corruption or lock contention has been enough of a recurring operational issue historically to warrant a manual fix-it button. Worth asking whether staff still use this regularly (a signal of how flaky the current data layer is under concurrent use).

**Dead code: Outlook calendar integration attempt.** A local procedure `Essai_Rappels` ("reminder test") builds a hardcoded calendar appointment via Outlook automation (`EmailOuvreSessionOutlook`, `RendezVousAjoute`) with dummy data (a 2010 meeting, fake participant "Romain"). It isn't called from any menu, button, or other procedure I've found — an abandoned proof-of-concept, likely an early idea to sync reminders to Outlook that was never finished or wired in. Flagging as obsolete/out of scope rather than a feature to replicate.

**Version display.** The home screen shows "Mafali V" + the compiled EXE's version metadata — simple, no notes needed beyond confirming a version string exists somewhere to migrate/replace.

---

## Module 9: Authentication & User Management (GPW / WDGPU) — in progress

**Architecture finding, stated up front: this looks like a generic, reusable AHG library, not Mafali-specific code.** The naming convention (`gpw*` function prefix, `GPWUtilisateur`/`GPWConfiguration`/`GPWElement` data sources, the constant password `"PCSGPW2001"` used to open the underlying file), consistent help-file branding under "Autefage," and its self-contained, business-logic-free design all point to this being AHG's standard groupware/auth component, dropped into multiple client projects rather than built specifically for Mafali. **My recommendation, to be confirmed once the rest of this module is reviewed: replace this wholesale with standard modern authentication (proper password hashing, session management, optional SSO) rather than port it — there is no Mafali-specific business value in replicating it faithfully, and (see below) it has real security weaknesses.**

### Screen: GPWLogin — the application's login screen

**Critical finding: passwords are stored and compared in plaintext (uppercased).** The password verification logic is a direct string comparison — `Majuscule(MotPasse) <> GPWUtilisateur.MotPasse` — against a field read straight from the user table. There is no hashing, salting, or any cryptographic protection at all. Passwords are also case-insensitive (forced to uppercase before comparison), which further weakens them. **This is the single most important security finding in the review so far and must not be replicated in the new system under any circumstances** — the new system needs proper password hashing (bcrypt/argon2/scrypt) from day one, and ideally this is treated as a hard requirement rather than a "faithful replication" discussion point.

**Other behaviors:**
- **Optional LDAP mode** (`gpwEnModeLDAP()`) — the component supports authenticating against an LDAP directory instead of the local password table. **Open question: does Mafali actually use LDAP, or local accounts only?** This materially affects the new system's auth architecture (needs to know if enterprise directory integration is a real requirement).
- **Weak lockout:** after 3 failed attempts (`nNBESSAISMAX = 3`), the login window simply closes — there's no persisted lockout period or attempt counter, so a user can immediately reopen the app and try again indefinitely. Not real brute-force protection.
- **First-login password setup:** new user accounts have a "password must be set" flag; on first login, the user is prompted to enter and confirm a new password, which then overwrites the plaintext-stored value.
- **Two login UI modes:** a "secure" mode (free-text login entry, the default) and a non-secure mode that instead shows a dropdown listing every registered username to choose from before entering a password — a minor information-disclosure pattern in the non-default mode, worth noting but likely low real-world impact if "secure" mode is what's actually configured.

### Screen: GPWMenuSuperviseur — post-login supervisor menu

**Purpose.** A small 4-button screen shown (presumably to users in the supervisor role, to be confirmed) after login: "Configurer le groupware" (opens GPWAssociationConfiguration, the user/config management screen), "Lancer l'application" (proceed into Fen_Principale), Fermer (quit), and Help. Confirms there's a distinct supervisor-level entry point separate from the standard login → main-menu flow, consistent with the "SUPERVISEUR" group already found gating filter visibility in Module 8.

### Screen: GPWFicheUtilisateur — Create/edit a user account

Fields: Login, Nom, Prénom, MotPasse (plaintext, see above), and a toggle "password must be set by user on first login." New users are automatically linked to a default "no configuration" (gpwAucun) role record for the current application. In LDAP mode, the name/password fields are grayed out (identity assumed to come from the directory). Confirms user accounts are scoped per-application (a `GPWUtilisateurConfiguration` row ties a Login to a Configuration for a specific named application, via `ProjetInfo()`) — i.e., this component is explicitly designed to manage users across multiple different applications from a shared user pool, not just Mafali.

### Screens: GPWFicheConfiguration / GPWChoixConfiguration / GPWDetailConfiguration — Role ("Configuration") management

**Concept.** A "Configuration" is this system's name for a role or permission profile. Configurations can be a **Group** (shared by multiple users, e.g. a department) or **individual** (auto-created and tied to exactly one user when a supervisor grants them custom permissions). Two configurations are hardcoded/reserved and not editable: **gpwSuperviseur** ("the supervisor has all rights on the application") and **gpwDéfaut**, the default group — **which also has all rights, unrestricted, by design**. This is an important detail: the granular permission system is opt-in. A user only has restricted access if a supervisor explicitly creates a custom Configuration for them or their group and then removes specific rights.

**GPWDetailConfiguration is a full per-window, per-UI-element permission editor.** For a selected Configuration, a supervisor picks any window/screen in the compiled application (the tool enumerates every window in the project automatically) and then sees every individual UI element in it — the system distinguishes types: group, menu, context menu, button, input field, label, list, toolbox, combo, selector, switch/toggle, table, toolbar, image, tab, panel, embedded-component ("superchamp"), other — and can toggle each one's enabled/visible state independently for that Configuration. Includes a "copy configuration" feature (clone one role's permissions to another) and cascading delete handling (deleting a group configuration prompts whether to also delete its associated user accounts).

**This is a materially more granular permission model than typical modern RBAC** (which usually gates at the screen or action/API level, not individual UI controls). **Key open question for you: does Mafali actually use this granularity today** — are there real custom Configurations restricting specific buttons/fields for specific staff — or does everyone effectively operate under the unrestricted default? If the latter, the new system only needs simple role-based screen/feature access, which is dramatically simpler to build and maintain than replicating this element-level engine.

### Screen: GPWAssociationConfiguration — User administration hub

**Purpose.** The central admin screen: lists all users with their assigned Configuration (role/group) in an editable dropdown per row. From here a supervisor can: create/edit/delete users (delete cascades to remove the user's role assignments first), create new Configurations, jump into the per-element permission editor for a user's group ("Modif_droits" — blocked with an info message for the two reserved configs, since they're not editable), and view connection history.

**LDAP bulk import.** A dedicated button imports all users from a connected LDAP directory in one action (confirmed via `gpwImportUtilisateursLDAP()`), only visible when LDAP mode is active — this is a real, built-out feature, not just a flag. **Reinforces the open question: is LDAP mode actually configured/used for Mafali, or is this a capability of the shared library that's unused here?**

### Screen: GPWHistoriqueConnexion — Login/connection history

**Purpose.** An audit viewer of login events, filterable by user/application and by date range (with a rich set of quick presets: today, yesterday, current/previous week, current/floating/previous month, current/floating/previous year), including a connections-per-hour chart. **Notable: a supervisor can wipe the entire connection history with one confirmation dialog** (`HCréation` recreates the file empty) — the same "audit trail that isn't tamper-evident" pattern already seen with Historique (Module 2) and with client deletion (Module 1). Consistent pattern worth a single deliberate architecture decision covering all of these at once, rather than fixing case by case.

### Internal component: WDGPU (Part 7 of the source document, ~404 pages)

I sampled rather than exhaustively read this section, and I'm confident that was the right call: it's the implementation layer underneath everything already documented above (FEN_GPU_Login, FEN_GPU_Principale, and a large shared procedure library). The procedure inventory alone (80+ functions) confirms this is a mature, general-purpose component: drag-and-drop group/user management, a plan/tab-based navigation framework, **its own internal schema-migration tooling** (`bMigrationDonnees`, `bMigrationDroits`, `bMigrationGroupes`, `bMigrationUtilisateurs`, `bInitialisationBase17*` — meaning this library has been upgraded across its own versions independently of Mafali), and usage statistics. This level of engineering — a component with its own migration history and multi-application user pool — is well beyond what any single client's bespoke CRM would justify building from scratch. **Conclusion: WDGPU is confirmed to be a shared AHG library, not Mafali-specific code, with no unique business value to preserve.**

### Recommendation for this module

**Replace, don't port.** Build the new system's authentication and authorization on a standard, modern foundation (proper password hashing, conventional session/token-based auth, optional SSO/LDAP integration if confirmed still needed) and a conventional role-based access control model scoped to screens/features. Only invest in fine-grained per-UI-element permissions if you confirm the business actually relies on that granularity today — my working assumption, pending your confirmation, is that most users operate under the unrestricted default and this granularity is rarely exercised in practice.

---

## Module 10: System/Framework Windows (quick pass, as scoped)

Sampled all ten rather than doing full deep dives, since these are exactly what they appear to be: **generic WinDev framework boilerplate with no Mafali business logic**, included in every WinDev project by default or used purely as internal plumbing. Confirmed via titles and code inspection:

- **WD_Imprimer / WD_Imprimer_Etat / WD_Installe_EtatsEtRequetes** — standard print dialog, print-job window, and a report/query deployment installer utility (used at build/deploy time to copy report definitions, not at runtime by end users).
- **APERCU** — the standard print-preview window ("Aperçu avant impression").
- **WD_Patience** — the standard "please wait" busy-indicator dialog.
- **WD_PopupSelCouleur / WD_PopupSelEpaisseur / WD_PopupZoom** — generic color picker, line-thickness picker, and zoom-level popups; standard WinDev widgets bundled by default, no evidence of being wired into any Mafali-specific feature.
- **WinDevDialogBox / WinDevMessageBox** — the framework's custom implementations of confirmation and message-box dialogs, used throughout the app wherever `Info()`, `Erreur()`, `Avertissement()`, or `Dialogue()` were called in the windows already documented.

**None of these require replication as distinct features** — in a web application, their equivalents (toast/alert components, a loading spinner, a print/export pipeline) are standard UI-library or browser-native functionality, not custom screens to design around.

---

## Module 11: Queries, Procedure Collections, Automated Tests (Parts 4–6)

### Queries (8 total, all reviewed with full SQL)

Confirms and closes out several open threads from earlier modules:

- **REQ_RappelRDV** (drives the startup reminder popup, Module 8): `WHERE RappelRDV = 1 AND Assistante_Commerciale = pAssistante AND Date_Rappel = pDate`. **Important, previously-hidden business rule: this is an exact date match, not "due on or before today."** A reminder only appears in the daily popup on the exact day it's set for — there's no "overdue" rollup for missed reminders. **Also: the query depends on a separate boolean field, France_Optique.RappelRDV, being explicitly set to true — and I have not yet found any code path in the windows already reviewed that sets this flag.** This is a genuine open question: is RappelRDV set somewhere I haven't seen, or is this a latent feature that doesn't actually get triggered by the normal Parcours_Client workflow (which only ever writes Date_Rappel/Heure_Rappel, not RappelRDV)? If the latter, the daily reminder popup may be showing far fewer (or zero) real reminders than staff expect. **Worth verifying directly with users: does the "today's reminders" popup actually show anything useful in practice?**
- **REQ_Vérif_Franchise**: confirms the auto-provisioning check from Module 7 exactly — counts existing Type_Franchise rows matching a name; the calling procedure (below) inserts a new one only if the count is 0.
- **REQ_CA_Derniers**: `SELECT TOP 5 ... FROM CA WHERE Cle_Opl = Param1`, sorted by year — the 5 most recent revenue-by-year rows for a client, with a display-formatted "Year ---> Amount" column. Used for the small CA summary list in Fen_Parcours_Client.
- **REQ_Tout_CA**: same table, no row limit — full revenue history, used in Fiche_CA.
- **Req_Main**: effectively a pass-through view over France_Optique (filterable by Famille + Cle_Opl, but called unfiltered in practice) — confirms it's just an indirection layer, not meaningfully different data.
- **Req_Histo, Req_Recherche_Partout, Req_Filtre_Operatrice**: match the field lists and parameters already documented in Modules 1–4; no new findings.

### Procedure collections (2 total)

- **GPWUtil**: holds all the helper functions used by the granular permission editor (Module 9) — confirms that entire feature's implementation lives partly here and partly in the internal WDGPU component, reinforcing it as a self-contained subsystem.
- **COL_ProcéduresGlobales**: small, and notably contains `Ajoute_TypeFranchise_Si_Non_Paramètré` — **confirmed implementation**: runs REQ_Vérif_Franchise, and if the count is 0, creates a new Type_Franchise row with `HRAZ` + `HAjoute`. Exactly matches the behavior inferred in Module 7. Also contains `Branchement_AHG_BDD`, which reads a database server connection config from a shared network INI file (`AHG_BDD.ini`) — confirms this application connects to shared AHG-managed database infrastructure (consistent with the `\\ARBAS`, `\\CANIGOU` server paths seen earlier), a deployment/infrastructure detail rather than a business rule.

**Unresolved dependency, confirmed.** `gsRechercherVille` (the postal-code-to-city lookup used in Modules 1 and 3) does not appear anywhere in this document outside its two call sites — it is not defined in either procedure collection, any window, or the internal component. **It's confirmed to be a genuinely external dependency** (likely a separate DLL or licensed component not captured in this technical export). I cannot determine its implementation from the available documentation. If this feature needs to be preserved, you'll need to identify the original external component (possibly a French postal-code/address lookup library) separately — it isn't in the 1602-page source.

### Automated tests

**There is exactly one trivial test scenario in the entire project** (a recorded mouse click closing Fen_Filtre_Historique), and the document's own summary states plainly: "There are no automated tests on the project." **This means there is no existing regression safety net or executable specification to lean on** — every behavior documented in this functional spec was reverse-engineered from reading source code and inferring intent, not validated against a test suite. Two implications: (1) manual user-acceptance testing against real staff workflows will be essential during migration, since there's no automated way to confirm "the new system behaves the same," and (2) the new system should be built with real automated test coverage from the start — a low-risk, high-value improvement with no faithful-replication downside.
