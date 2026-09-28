# Change requests — 2026-09-03

Organized from Badr's notes after Bruno's first round of testing. Nothing here
is implemented yet — this is the list to go through together first. Each item
below includes what I found when checking the current code/data, and any open
question that needs a decision before I build it.

---

## 1. Permissions

### 1a. Commercial role: restrict page access

**Current state**: almost nothing in the app is role-gated. The only
existing restriction is `HistoriqueConnexions.razor`
(`[Authorize(Roles = "admin")]`) and the "Historique des Connexions" nav
item. Every other page — including Clients, Familles, Franchises, Pays,
Assistantes, both filter builders, both filter-management screens, and
Fichier des Historiques — is currently open to *any* logged-in user,
Commercial included.

**Requested**: Commercial should only be able to reach:
- Filtres Prédéfinis
- Rechercher un Client
- Parcours Client (not its own nav link today — reached via the other
  screens above, e.g. double-clicking a row. Stays reachable as long as
  the screens that link to it stay reachable.)
- Rappels
- Bon de Commande — **note**: this is currently the *template upload/
  management* page (`/bon-commande`, `BonCommandePage.razor`), where
  someone uploads the source `.xlsx` files for the Optique and Revendeur
  templates. That matches "to choose source of template of both files" —
  flagging just to confirm that's really the intent, since it's a bit
  unusual for Commercial to manage template *files* while being locked
  out of client/reference-data screens. If instead you meant "Commercial
  should be able to generate a Bon de Commande for a client" — that's a
  different, already-Commercial-reachable action (the button lives on
  Parcours Client, not this page) and needs no separate permission.

**Would need to become admin/assistant-only** (currently open to
everyone): Fichier des Clients, Familles, Franchises, Pays, Assistantes,
Définir un Filtre, Définir un Filtre Historique, Fichier des Filtres,
Fichier des Filtres Historique, Fichier des Historiques.

**Open question**: should Assistant have the same restricted set as
Commercial, or keep its current full access? Not stated either way — the
request only mentions Commercial.

### 1b. Commercial cannot toggle Bloqué

**Current state**: confirmed — the "Bloqué" checkbox on Parcours Client
(`ParcoursClientPage.razor`, the one right next to Etat Client) has *no*
disabled condition at all today. Every role, including Commercial, can
currently freely check/uncheck it. This is a real, currently-missing
restriction, not a tightening of an existing one.

**Requested**: Commercial loses the ability to change it (view-only for
that role; Admin/Assistant unaffected).

---

## 2. Etat Client

**Current state**: a 3-option dropdown — `0` = "Non défini" (the
default when a client has no value), `1` = "Client", `2` = "Prospect".

**Requested**: remove "Non défini" entirely; default becomes "Prospect"
until explicitly changed to "Client".

**Real-data wrinkle worth deciding on now**: I checked the live
`etat_client` values across all 93,234 clients — it's messier than the
clean 0/1/2 the dropdown assumes:

| Value | Count | Currently shown as |
|---|---|---|
| `0` | 29,401 | Non défini |
| `2` | 27,560 | Prospect |
| *(empty string)* | 19,879 | **not matched by any option today** |
| `1` | 10,052 | Client |
| `-1` | 6,330 | **not matched by any option today** |
| `Prospet` (typo) | 10 | **not matched — literal data-entry typo** |
| `Client` (literal text) | 2 | **not matched — should be `1`, not the word** |

So "remove Non défini" raises a real scope question: does this mean —
- **(a)** just remove it as a *selectable* option going forward (new
  edits can't pick it, but existing `0`/empty/`-1`/typo rows stay as-is
  until someone happens to open and re-save that client), or
- **(b)** a one-time cleanup pass that rewrites all ~55,600 non-clean
  rows (`0` + empty + `-1` + the typo + literal "Client") to a real
  value now?

I'd lean toward (a) as the safer default (no bulk data rewrite), but
this is your call, not mine to assume.

### Resolved 2026-09-04

Went with **(a)**: cosmetic-only. Junk values (`0`/empty/`-1`/typos)
always *display* as Prospect wherever Etat Client appears, but the raw
DB value is left untouched unless the user explicitly changes the
dropdown. No bulk rewrite of the ~55,600 non-clean rows. New clients
default to Prospect (`2`) at creation. Implemented and verified live —
see `PROGRESS.md`, 2026-09-04 entry.

**Known gap, left as-is for now**: because the dropdown only writes
back on a real `change` event, there's no single click that explicitly
"confirms" Prospect for a junk row — clicking the option already shown
(e.g. "Prospect" for a raw `0`) doesn't register as a change, so the
underlying junk value is never actually cleaned up that way. Someone
wanting to genuinely normalize a row would have to switch to "Client"
then back to "Prospect" (two changes) to get a clean `2` written. No
action requested on this for now — noting it here so it isn't
forgotten.

---

## 3. Générer un fichier Bon de Commande

**Current state** (`ParcoursClientPage.razor`, the "Générer fichier bon
de commande" panel): shows two buttons, **Optique** and **Revendeur** —
whoever's using the screen picks one manually, calling
`GenerateBonCommande(BonCommandeTemplateType.Optique)` or `...Revendeur`
directly.

### 3a. Auto-select the template from Type de Famille

**Requested**: no manual choice — if `Type de Famille` starts with the
word "Opticien" as its first word, use the Optique template; otherwise
Revendeur.

Checked the real `Famille` values already in use — this rule lines up
cleanly with the real data: `Opticien FRANCE`, `Opticien Allemagne`,
`Opticien Andorre`, `Opticien Belgique`, `Opticien Espagne`, `Opticien
Luxembourg`, `Opticien Maroc`, `Opticien Pays Bas`, `Opticien SUISSE` are
all real values, all genuinely starting with "Opticien " as a distinct
first word. Everything else (`AGENCE PUB`, `Bijouterie`, `CENTRALE
OPTIQUE`, `Lunetier`, `SOCIETE`, `Trail Running`, etc.) doesn't start
with it, so falls to Revendeur.

**Open question**: what happens for a client with *no* Famille set at
all (blank)? Not addressed in the request — I'd default that case to
Revendeur (same "otherwise" branch), but flagging it explicitly rather
than silently deciding.

### 3b. Reorder the address block

**Current order** (`BonCommandeTemplateService.BuildAddressLines`):
1. Raison Sociale
2. Rue
3. `{CP} {Ville}` combined
4. Localisation 1 (only appended if non-blank)

**Requested order**:
1. Raison sociale
2. Adresse/Rue
3. Localisation 1
4. CP Ville

So this is a genuine reorder — Localisation 1 moves from last to third,
ahead of the CP+Ville line, not just a relabeling. Applies to both the
Optique and Revendeur fill paths (they share `BuildAddressLines`).

### 3c. Fill the Contact field with Responsable Achats

**Current state**: confirmed — the Contact cell (`C22` on the Optique
template, `C25` on Revendeur) is explicitly left blank today. The code
comment literally says this was deliberate: *"not decided yet, don't
guess"* — Badr hadn't specified what should go there. This closes that
open item: Contact = `Client.ResponsableAchat`, on both templates.

---

## Summary of what's genuinely new vs. tightening

- **New capability, doesn't exist at all yet**: role-based page
  restriction (1a) — currently only one page in the whole app is
  role-gated.
- **New restriction on an existing unrestricted field**: Bloqué
  checkbox (1b).
- **Real data-scope decision needed before building**: Etat Client
  cleanup (2).
- **Clarification needed**: Bon de Commande page's role in item 1a
  (template management vs. document generation), and the no-Famille
  edge case in 3a.
- **Everything else** (2's dropdown/default change, 3a/3b/3c) is a
  clean, well-scoped code change with no ambiguity once the two open
  questions above are answered.
