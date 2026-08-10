# Mafali CRM — Domain Model v1 (Draft)

Source: `Mafali_Optique.pdf`, Part 2 "Schéma des données" (pages 10–25), cross-checked against rendered page images for key indicators (checkbox columns don't survive plain text extraction). Underlying WinDev analysis file: `Autefage.ana\Autefage.wda` — note the internal name is still "Autefage," the CRM's likely predecessor/original name (**open question**, see below).

Legend: **Fact** = directly stated in the doc. **Inferred** = my reading of naming/typing, not explicitly declared. **Unknown** = needs your confirmation.

---

## 1. Entity inventory (9 tables, 76 fields total, 0 formal relationships declared)

**Fact:** the WinDev analysis reports `Nb. liaisons: 0` — zero formal foreign keys exist anywhere in this schema. Every relationship below is inferred from field naming/typing, not enforced by the database. This is the single biggest structural fact about this system: all referential integrity currently lives in application code (WLangage), if it exists at all.

### France_Optique — the client master table
Label: "Fichier France Optique." Despite the generic name, this is the core **Client/Prospect** entity. 45 fields, record size 1619 bytes.

- **Cle_Opl** (unsigned 4-byte int, default 0) — **Clé Unique** (confirmed via image). This is the true primary key. Not an auto-identifier type — **inferred** to be assigned by application code rather than the DB engine (worth confirming in Part 3 window code).
- Identity/address: Raison_Sociale, Complement, Rue, Localisation_1, Localisation_2, CP, Ville, Pays (free text, 30 chars)
- Contact: Telephone, Portable, TelBis, Fax, Email
- Classification: Famille (→ Type_Famille, by text), Franchise / Franchise2 / Franchise3 / Franchise4 (four separate franchise fields — **unknown**: multi-franchise assignment per client, or legacy field reuse? needs your input)
- People: Assistante_Commercial (→ Assistantes, by text), Responsable_Achat, Représentant
- Pipeline/status: Op_En_Cours, **Status_Vente**, **Statuts_Clients**, **EtatClient** — three distinct status-like fields (**unknown**: need the actual business meaning of each; can't infer safely)
- Notes: Note (250 chars) and NotePerm (unlimited memo) — two separate note fields, purpose of the split unclear (**unknown**)
- Flags: bloque (blocked/blacklisted), RappelRDV (reminder flag), Production (**unknown** — purpose unclear from name alone)
- Scheduling: Date_Saisie/Heure_Saisie (created), Date_Rappel/Heure_Rappel (next follow-up) — indexed as composite key `D_H_Rappel`, confirming a reminder/tickler feature is central to this CRM
- A 185-byte composite index `CleComp_Telep_Statu_Porta_Op_En_Fa` combines Telephone+Status_Vente+Portable+Op_En_Cours+Fax+Date_Saisie+Date_Rappel+Cle_Opl+CP — **unknown** purpose; likely supports a specific search/duplicate-detection screen. Will confirm once I read the relevant window in Part 3.

### Historique — interaction log per client
19 fields, heavily **denormalized**: it duplicates Raison_Sociale, CP, Ville, Franchise, Statuts_Clients, Magasin_Principal, Assistante_Commercial and Status_Vente as a snapshot at the time each entry was made, rather than only storing a client reference.

- **Id_Histo** (auto-identifier, 8 bytes) — **Clé Unique**, confirmed true primary key
- **Num_Client** (*signed* 4-byte int) — indexed but not unique, default 0. **Inferred** link to France_Optique.Cle_Opl, but note the type mismatch: Cle_Opl is *unsigned*, Num_Client is *signed*. Likely harmless in practice (values stay positive) but flags this as code-enforced, not schema-enforced.
- Opération (free text — the action/interaction type recorded)
- FicStk — binary blob (Image/mémo binaire) stored directly in the row: each history entry can carry an attached file/image
- **Design decision needed:** is the denormalization intentional (preserve "what we knew about the client at the time," e.g. if their address/status later changes)? Or an artifact of not joining? This materially affects whether the new system should replicate point-in-time snapshots or normalize to FK + separate audit trail. **Question for you.**

### CA — annual revenue per client
Simple fact table. IDCA (auto PK, unique). Année + CA (revenue as signed 4-byte integer — **unknown**: whole currency units or could this lose sub-unit precision? need confirmation). Cle_Opl links to client (unsigned int, indexed non-unique). Composite index Cle_Opl+Année exists but is marked **Doublon (non-unique)**, not unique — meaning the database does **not** prevent two revenue rows for the same client/year; that constraint, if it exists at all, is enforced only in application code. Flag as an integrity risk to check before migration (are there duplicate client/year rows in the live data today?).

### Assistantes — sales assistants/reps
Prénom_Nom, Service, All_Filtres (boolean — "access to all filters" permission), Commentaire. **No auto-identifier and no unique key at all** (confirmed via image — Prénom_Nom is indexed but not unique). Assistants are referenced elsewhere purely by name string (France_Optique.Assistante_Commercial, Historique.Assistante_Commercial). This is a real integrity gap: nothing stops two assistants sharing a name, and renaming one doesn't cascade anywhere. Table-level stats also suggest Type_Famille and Type_Franchise share this same gap (no auto-id, no unique key) — will confirm.

### Filtre_Opératrice / Filtre_Historique — saved search filters
Per-operator saved filters for the client list (Filtre_Opératrice: Nom_Operateur + Nom_Filtre + Filtre_Réel, a 512-char stored filter expression) and for the history list (Filtre_Historique: same shape, minus the operator field — filters here appear to be global/shared rather than per-user; **unknown** why the asymmetry). Filtre_Réel is presumably a serialized WLangage filter/query condition — I'll need to see actual stored values or the generating code to know its format.

### Pays — country reference data
IDPays (auto PK), NomPays, Indicatif (dialing code), **Masque** (phone number format mask per country) — this tells us phone field validation/formatting is meant to vary by selected country, a real business rule to preserve. Note: France_Optique.Pays is a free-text field (30 chars), not a foreign key to Pays.IDPays — country is duplicated as text, not referenced by ID.

### Type_Famille / Type_Franchise — simple lookup lists
Single-field reference tables (Nom_Famille, Nom_Franchise) backing the Famille and Franchise dropdowns on France_Optique. Same free-text-not-FK pattern as Pays.

---

## 2. Inferred relationships (none are DB-enforced — 0 declared liaisons)

| From | To | Join field(s) | Confidence |
|---|---|---|---|
| Historique.Num_Client | France_Optique.Cle_Opl | int match (signed/unsigned mismatch) | High (inferred) |
| CA.Cle_Opl | France_Optique.Cle_Opl | int match | High (inferred) |
| France_Optique.Franchise / 2/3/4 | Type_Franchise.Nom_Franchise | text match | Medium |
| France_Optique.Famille | Type_Famille.Nom_Famille | text match | Medium |
| France_Optique.Pays | Pays.NomPays | text match | Medium |
| France_Optique/Historique.Assistante_Commercial | Assistantes.Prénom_Nom | text match | Medium |
| Filtre_Opératrice.Nom_Operateur | Assistantes.Prénom_Nom (or a separate user/login concept?) | text match | **Low — unknown, could instead map to WDGPU's user model** |

Because every one of these is a text or loosely-typed match with no DB constraint, none of them can be assumed reliable until I read the actual WLangage code in Part 3/4 that performs the joins — the schema alone can't tell us if orphaned/inconsistent values exist today.

---

## 3. Key findings to resolve before the domain model is final

1. **Three status fields on France_Optique** (Status_Vente, Statuts_Clients, EtatClient) — need business definitions and how each transitions.
2. **Four franchise fields** (Franchise, Franchise2-4) — multi-value or legacy artifact?
3. **No enforced uniqueness** on Assistantes (and likely Type_Famille/Type_Franchise) — migration will need a decision on backfilling surrogate keys and deduplication.
4. **CA uniqueness (client+year) is not DB-enforced** — need to check for existing duplicate data.
5. **Historique's denormalization** — intentional point-in-time snapshot, or replicate as normalized audit? Affects DB design directly.
6. Two note fields on France_Optique (Note vs NotePerm) — distinct purposes?
7. `Production` field on France_Optique — meaning unclear from name.
8. The stale Excel OLEDB connection (`C:\Documents and Settings\fe\Bureau\Autefage\Fichier France Optique.xlsx`) confirms an Excel import/export feature tied to a specific old machine — purely a technical migration item (windows `Fen_Ajout_Depuis_Excel` / `FEN_Modification_Depuis_Excel` reference this), not a business rule, but worth knowing if this import is still used routinely.
9. Naming: is "Autefage" (the underlying schema/database name) the predecessor product Mafali was rebranded from, still in use elsewhere? — carried over from my first question, still open.

---

## 4. What's next

Part 3 (pages 26–1138) has the 46 windows with embedded code — that's where I can confirm or correct every "Medium/Low confidence" relationship above, and get real answers to the status-field and franchise-field questions by reading how each is used in the UI. I'll work through it grouped by function (client management, history/CA tracking, reminders, filters, reference data admin) rather than page order, and treat Part 7 (WDGPU, 404 pages) as a separate infrastructure/auth track pending your answer on whether it should be ported as-is.
