"""
Shared helpers for the migration/load_*.py scripts — all of which read the
new lowercase-header CSV export format (produced by whatever script/tool
generated CRM_Export, confirmed structurally sound across every table by
direct inspection before any loader was written: field counts checked
against every row, not just a sample, and cross-referenced against the
Attachments folder for historique specifically).

Two format quirks every loader has to handle, both already diagnosed
against real sample data before this file was written:

  1. Some files start with a `sep=,` line (an Excel delimiter hint) before
     the real header row; some don't. row_reader() skips it either way if
     present.
  2. Trailing columns that are empty for a given row are sometimes omitted
     entirely rather than written as empty fields (confirmed against
     france_optique_export.csv: rows with fewer fields than the header
     always match the header's columns correctly from the start, just cut
     short — never shifted or reordered). pad_row() re-pads a short row
     with empty strings before it's mapped to column names, so a loader
     never has to special-case "this row is missing its last N columns."
"""

import csv
import datetime


def row_reader(path):
    """Yields (header, row) pairs from a CSV, skipping a leading `sep=,`
    line if present. header is yielded once (as the first item, sort of —
    see load_*.py callers, which pull it via next())."""
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        for row in reader:
            if row and row[0] == "sep=":
                continue
            yield row


def pad_row(row, width):
    if len(row) >= width:
        return row
    return row + [""] * (width - len(row))


def clean(v):
    v = v.strip() if isinstance(v, str) else v
    return v if v else None


def make_clean_limited(limit, col_label):
    """Returns a clean()-like function that also truncates to `limit`
    chars, printing a warning when it actually has to. Real data found:
    a small set of otherwise-legitimate historique rows have exactly one
    corrupted, garbled-binary-looking field that overflows its VARCHAR
    limit by a few characters (confirmed by inspection — the field
    content itself is nonsense control characters, not real business
    text) while the rest of the row (client number, dates, notes, city)
    is completely real and worth keeping. Truncating just the bad field
    preserves the real row instead of discarding it wholesale."""
    def fn(v):
        v = clean(v)
        if v is None or len(v) <= limit:
            return v
        print(f"  WARNING: {col_label} value too long ({len(v)} > {limit} chars), "
              f"truncating: {v[:30]!r}...")
        return v[:limit]
    return fn


def parse_date(v):
    v = clean(v)
    if v is None:
        return None
    try:
        return datetime.date.fromisoformat(v)
    except ValueError:
        print(f"  WARNING: unparseable date {v!r}, storing as NULL")
        return None


def parse_time(v):
    v = clean(v)
    if v is None:
        return None
    try:
        return datetime.time.fromisoformat(v)
    except ValueError:
        print(f"  WARNING: unparseable time {v!r}, storing as NULL")
        return None


def parse_int(v):
    v = clean(v)
    return int(v) if v is not None else None


def parse_bool(v):
    """Every boolean column this feeds (bloque, rappel_rdv, all_filtres)
    is NOT NULL DEFAULT false in the schema — a blank source value means
    "not set," which this schema already treats as equivalent to false,
    not as a real NULL. Returning None here for a blank value would
    violate the NOT NULL constraint instead of falling back to the
    column's own default."""
    v = clean(v)
    if v is None:
        return False
    return v not in ("0", "false", "False", "FALSE")
