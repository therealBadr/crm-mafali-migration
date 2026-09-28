using System.Globalization;

namespace MafaliCrm.Web.Services;

// Interprets Filtre_Réel text — the raw WinDev-style filter strings staff
// type by hand into Fen_Detail_Filtre (FiltreDetailModal.razor, unchanged by
// this parser — it only ever reads what's already saved). Grammar below was
// built from what's actually present across all 66 real saved
// filtre_operatrice rows, verified directly against the live data, not
// assumed: only "et"/"ou" (any case) as logical connectors; "=", "<>", "<",
// "<=", ">", ">=", "LIKE", "NOT LIKE", "BETWEEN...AND..." as operators; and
// "SYSDATE" as a literal. Fails loudly (FormatException) on anything outside
// that vocabulary rather than silently misreading it — a saved filter that
// can't be parsed still saves and displays exactly as typed, it just can't
// be run by the "Filtres prédéfinis" page until the text or the parser is
// fixed.
//
// One real inferred behavior, confirmed by real usage rather than guessed:
// a value containing '%' is treated as a wildcard pattern regardless of
// which operator token was used — e.g. real data has "Famille ='%Agence
// Pub%'" (equals, not LIKE) reused identically across 15 saved filters by
// four different staff members, which only makes sense if the legacy engine
// treats '%' as a wildcard under '=' too, not just under LIKE.
public static class FiltreReelParser
{
    public static (string Predicate, object[] Args) Parse(string filtreReel) =>
        Parse(filtreReel, LegacyNameToFieldKey, ClientFilterService.Fields, ClientFilterService.PropertyName);

    // Historique's own field vocabulary is smaller than Client's and lives
    // in HistoriqueFilterService instead — same grammar, same operator
    // templates, just resolved against a different table's field maps so a
    // saved Historique filtre_reel parses against Historique's own columns
    // (e.g. "Num_Client" resolves here, not against France_Optique's
    // Cle_Opl, which Historique has no equivalent field for at all).
    public static (string Predicate, object[] Args) ParseHistorique(string filtreReel) =>
        Parse(filtreReel, HistoriqueLegacyNameToFieldKey, HistoriqueFilterService.Fields, HistoriqueFilterService.PropertyName);

    private static (string Predicate, object[] Args) Parse(
        string filtreReel,
        Dictionary<string, string> legacyNameToFieldKey,
        List<FilterField> fields,
        Dictionary<string, string> propertyName)
    {
        var tokens = Tokenize(filtreReel);
        var pos = 0;
        var args = new List<object>();
        var predicate = ParseOr(tokens, ref pos, args, filtreReel, legacyNameToFieldKey, fields, propertyName);
        if (pos != tokens.Count)
        {
            throw new FormatException($"Unexpected \"{tokens[pos].Text}\" in filter: \"{filtreReel}\"");
        }
        return (predicate, args.ToArray());
    }

    // ---- Tokenizer ----

    private enum TokenType { LParen, RParen, Ident, String, Op, Like, Not, Between, And, Et, Ou, Sysdate }

    private record Token(TokenType Type, string Text);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { tokens.Add(new(TokenType.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new(TokenType.RParen, ")")); i++; continue; }

            if (c == '\'')
            {
                var j = i + 1;
                while (j < s.Length && s[j] != '\'') j++;
                if (j >= s.Length)
                {
                    throw new FormatException($"Unterminated string in filter: \"{s}\"");
                }
                tokens.Add(new(TokenType.String, s.Substring(i + 1, j - i - 1)));
                i = j + 1;
                continue;
            }

            if (c == '<')
            {
                if (i + 1 < s.Length && s[i + 1] == '>') { tokens.Add(new(TokenType.Op, "<>")); i += 2; }
                else if (i + 1 < s.Length && s[i + 1] == '=') { tokens.Add(new(TokenType.Op, "<=")); i += 2; }
                else { tokens.Add(new(TokenType.Op, "<")); i++; }
                continue;
            }
            if (c == '>')
            {
                if (i + 1 < s.Length && s[i + 1] == '=') { tokens.Add(new(TokenType.Op, ">=")); i += 2; }
                else { tokens.Add(new(TokenType.Op, ">")); i++; }
                continue;
            }
            if (c == '=') { tokens.Add(new(TokenType.Op, "=")); i++; continue; }

            if (char.IsLetter(c) || c == '_')
            {
                var j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                var word = s.Substring(i, j - i);
                i = j;
                tokens.Add(word.ToUpperInvariant() switch
                {
                    "ET" => new Token(TokenType.Et, word),
                    "OU" => new Token(TokenType.Ou, word),
                    "LIKE" => new Token(TokenType.Like, word),
                    "NOT" => new Token(TokenType.Not, word),
                    "BETWEEN" => new Token(TokenType.Between, word),
                    "AND" => new Token(TokenType.And, word),
                    "SYSDATE" => new Token(TokenType.Sysdate, word),
                    _ => new Token(TokenType.Ident, word),
                });
                continue;
            }

            throw new FormatException($"Unexpected character '{c}' in filter: \"{s}\"");
        }
        return tokens;
    }

    // ---- Recursive-descent parser, building the Dynamic LINQ predicate
    //      string directly (no separate AST — nothing else consumes it). ----

    // orExpr := andExpr (OU andExpr)*
    private static string ParseOr(List<Token> tokens, ref int pos, List<object> args, string original,
        Dictionary<string, string> legacyNameToFieldKey, List<FilterField> fields, Dictionary<string, string> propertyName)
    {
        var parts = new List<string> { ParseAnd(tokens, ref pos, args, original, legacyNameToFieldKey, fields, propertyName) };
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Ou)
        {
            pos++;
            parts.Add(ParseAnd(tokens, ref pos, args, original, legacyNameToFieldKey, fields, propertyName));
        }
        return parts.Count == 1 ? parts[0] : "(" + string.Join(" || ", parts) + ")";
    }

    // andExpr := term (ET term)*
    private static string ParseAnd(List<Token> tokens, ref int pos, List<object> args, string original,
        Dictionary<string, string> legacyNameToFieldKey, List<FilterField> fields, Dictionary<string, string> propertyName)
    {
        var parts = new List<string> { ParseTerm(tokens, ref pos, args, original, legacyNameToFieldKey, fields, propertyName) };
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Et)
        {
            pos++;
            parts.Add(ParseTerm(tokens, ref pos, args, original, legacyNameToFieldKey, fields, propertyName));
        }
        return parts.Count == 1 ? parts[0] : "(" + string.Join(" && ", parts) + ")";
    }

    // term := '(' orExpr ')' | comparison
    private static string ParseTerm(List<Token> tokens, ref int pos, List<object> args, string original,
        Dictionary<string, string> legacyNameToFieldKey, List<FilterField> fields, Dictionary<string, string> propertyName)
    {
        if (pos >= tokens.Count)
        {
            throw new FormatException($"Unexpected end of filter: \"{original}\"");
        }
        if (tokens[pos].Type == TokenType.LParen)
        {
            pos++;
            var inner = ParseOr(tokens, ref pos, args, original, legacyNameToFieldKey, fields, propertyName);
            Expect(tokens, ref pos, TokenType.RParen, original);
            return inner;
        }
        return ParseComparison(tokens, ref pos, args, original, legacyNameToFieldKey, fields, propertyName);
    }

    // comparison := IDENT ('=' | '<>' | '<' | '<=' | '>' | '>=' | LIKE | NOT LIKE) value
    //             | IDENT BETWEEN value AND value
    private static string ParseComparison(List<Token> tokens, ref int pos, List<object> args, string original,
        Dictionary<string, string> legacyNameToFieldKey, List<FilterField> fields, Dictionary<string, string> propertyName)
    {
        var fieldToken = Expect(tokens, ref pos, TokenType.Ident, original);
        if (!legacyNameToFieldKey.TryGetValue(fieldToken.Text.ToUpperInvariant(), out var fieldKey))
        {
            throw new FormatException($"Unknown field \"{fieldToken.Text}\" in filter: \"{original}\"");
        }
        var meta = fields.First(f => f.Field == fieldKey);
        var prop = propertyName[fieldKey];

        if (pos < tokens.Count && tokens[pos].Type == TokenType.Between)
        {
            pos++;
            var v1 = CastLegacyValue(meta.Type, ParseValueToken(tokens, ref pos, original), original);
            Expect(tokens, ref pos, TokenType.And, original);
            var v2 = CastLegacyValue(meta.Type, ParseValueToken(tokens, ref pos, original), original);
            var i1 = args.Count; args.Add(v1);
            var i2 = args.Count; args.Add(v2);
            return $"({prop} >= @{i1} && {prop} <= @{i2})";
        }

        Token opToken;
        var negated = false;
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Not)
        {
            negated = true;
            pos++;
            opToken = Expect(tokens, ref pos, TokenType.Like, original);
        }
        else
        {
            opToken = Expect(tokens, ref pos, TokenType.Op, TokenType.Like, original);
            if (opToken.Type == TokenType.Op && opToken.Text == "<>") negated = true;
        }

        var raw = ParseValueToken(tokens, ref pos, original);

        if (meta.Type == FieldType.Text && raw.Contains('%'))
        {
            return BuildWildcardClause(prop, raw, negated, args);
        }

        // "" is HFSQL's blank, same convention as BuildWildcardClause's
        // all-'%' case above — comparing directly against it means "is
        // blank" / "is not blank", the opposite null-handling from every
        // other Eq/Ne comparison (where blank, migrated to NULL, passes
        // Ne but not Eq — correct for "Note <> '9999'", since a blank
        // note genuinely isn't "9999"). A real saved filter surfaced the
        // difference: "Telephone NOT LIKE ''" means "has an actual phone
        // number" — blank/NULL telephone must NOT pass that, confirmed
        // against real data (see BuildBlankClause).
        if (meta.Type == FieldType.Text && raw == "")
        {
            return BuildBlankClause(prop, MapComparisonOperator(opToken, negated), args);
        }

        var value = CastLegacyValue(meta.Type, raw, original);
        var index = args.Count; args.Add(value);
        return ClientFilterService.OperatorTemplate(prop, ClientFilterService.EffectiveOperator(MapComparisonOperator(opToken, negated), raw), $"@{index}", meta.Type, args);
    }

    // Bypasses OperatorTemplate/Fold entirely, same reasoning as
    // BuildWildcardClause bypassing it for the all-'%' shape — this is a
    // sentinel comparison ("is/isn't blank"), not a real value comparison,
    // so the usual case/accent folding is irrelevant and the null-handling
    // needs to run backwards from every other Eq/Ne (see the comment
    // above). Only Eq/Ne are meaningful against blank — Gt/Lt/etc. against
    // "" never appear in real data and aren't guessed at here.
    private static string BuildBlankClause(string prop, Operator op, List<object> args)
    {
        var index = args.Count; args.Add(string.Empty);
        return op switch
        {
            Operator.Eq => $"({prop} == null || {prop} == @{index})",
            Operator.Ne => $"({prop} != null && {prop} != @{index})",
            _ => throw new FormatException($"Unsupported operator for a blank ('') comparison on field \"{prop}\"."),
        };
    }

    private static Operator MapComparisonOperator(Token opToken, bool negated) => opToken switch
    {
        { Type: TokenType.Op, Text: "=" } => Operator.Eq,
        { Type: TokenType.Op, Text: "<>" } => Operator.Ne,
        { Type: TokenType.Op, Text: "<" } => Operator.Lt,
        { Type: TokenType.Op, Text: "<=" } => Operator.Lte,
        { Type: TokenType.Op, Text: ">" } => Operator.Gt,
        { Type: TokenType.Op, Text: ">=" } => Operator.Gte,
        // LIKE/NOT LIKE with no '%' in the value degenerates to plain
        // (in)equality — there's no wildcard character left to match on.
        { Type: TokenType.Like } => negated ? Operator.Ne : Operator.Eq,
        _ => throw new FormatException($"Unsupported operator \"{opToken.Text}\"."),
    };

    // Trailing-only '%' -> StartsWith, both-sides '%...%' -> Contains — the
    // only two shapes present anywhere in the real 66 saved filters. A
    // leading-only pattern ('%text') never appears in real data, so it's
    // treated as unsupported rather than guessed at.
    //
    // A pattern that's nothing but '%' (one or more) is its own case, kept
    // separate from the Contains/StartsWith path below: HFSQL's LIKE '%'
    // means "matches any value, including no value at all" — it includes
    // NULLs. Postgres/EF's null-guarded Contains (OperatorTemplate's
    // "prop != null && prop.Contains(...)") would silently exclude every
    // NULL row instead, which is exactly the discrepancy Badr reported —
    // (Famille='Opticien FRANCE') et (Email LIKE '%') gave 18,650 in the
    // legacy system vs 13,776 in Postgres, a gap of precisely the 4,874
    // clients whose Email is NULL. Bypassing OperatorTemplate entirely for
    // this shape (rather than adding an "OR prop IS NULL" branch to
    // Contains generally) keeps every other real Contains/StartsWith usage
    // — which must still exclude NULLs, since NULL genuinely doesn't
    // contain any real substring — unchanged.
    private static string BuildWildcardClause(string prop, string raw, bool negated, List<object> args)
    {
        if (raw.Length > 0 && raw.All(c => c == '%'))
        {
            return negated ? "false" : "true";
        }

        var leading = raw.StartsWith('%');
        var trailing = raw.EndsWith('%');
        // Computed from the original bounds, not chained on the same
        // variable — a 1-char value like "%" is both leading and trailing
        // at once, and stripping sequentially would strip it twice.
        var start = leading ? 1 : 0;
        var end = raw.Length - (trailing ? 1 : 0);
        var stripped = end > start ? raw[start..end] : "";

        Operator op;
        if (leading && trailing) op = negated ? Operator.NotContains : Operator.Contains;
        else if (trailing) op = negated ? Operator.NotStartsWith : Operator.StartsWith;
        else throw new FormatException($"Unsupported wildcard position in value \"{raw}\" (only \"text%\" and \"%text%\" are supported).");

        var index = args.Count; args.Add(stripped);
        // Always Text — this method is only ever called from the
        // meta.Type == FieldType.Text branch above.
        return ClientFilterService.OperatorTemplate(prop, op, $"@{index}", FieldType.Text, args);
    }

    private static string ParseValueToken(List<Token> tokens, ref int pos, string original)
    {
        if (pos >= tokens.Count)
        {
            throw new FormatException($"Expected a value in filter: \"{original}\"");
        }
        var t = tokens[pos];
        if (t.Type == TokenType.String) { pos++; return t.Text; }
        if (t.Type == TokenType.Sysdate) { pos++; return "SYSDATE"; }
        throw new FormatException($"Expected a value but found \"{t.Text}\" in filter: \"{original}\"");
    }

    private static object CastLegacyValue(FieldType type, string raw, string original)
    {
        if (raw == "SYSDATE")
        {
            if (type != FieldType.Date)
            {
                throw new FormatException($"SYSDATE used on a non-date field in filter: \"{original}\"");
            }
            return DateOnly.FromDateTime(DateTime.Today);
        }

        return type switch
        {
            // Legacy dates are unseparated "YYYYMMDD" digits, never ISO —
            // matches every real Date_Rappel value found in the live data.
            FieldType.Date => DateOnly.ParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture),
            FieldType.Number => long.Parse(raw),
            // Time/Boolean never appear in any of the 66 real saved filters —
            // best-effort support, unverified against real usage.
            FieldType.Time => TimeOnly.ParseExact(raw, "HHmm", CultureInfo.InvariantCulture),
            FieldType.Boolean => raw is "1" or "Vrai" or "VRAI",
            _ => raw,
        };
    }

    private static Token Expect(List<Token> tokens, ref int pos, TokenType type, string original)
    {
        if (pos >= tokens.Count || tokens[pos].Type != type)
        {
            var found = pos < tokens.Count ? tokens[pos].Text : "end of filter";
            throw new FormatException($"Expected {type} but found \"{found}\" in filter: \"{original}\"");
        }
        return tokens[pos++];
    }

    private static Token Expect(List<Token> tokens, ref int pos, TokenType a, TokenType b, string original)
    {
        if (pos >= tokens.Count || (tokens[pos].Type != a && tokens[pos].Type != b))
        {
            var found = pos < tokens.Count ? tokens[pos].Text : "end of filter";
            throw new FormatException($"Expected an operator but found \"{found}\" in filter: \"{original}\"");
        }
        return tokens[pos++];
    }

    private static readonly Dictionary<string, string> LegacyNameToFieldKey =
        ClientFilterService.LegacyFieldName.ToDictionary(
            kv => kv.Value.ToUpperInvariant(),
            kv => kv.Key);

    private static readonly Dictionary<string, string> HistoriqueLegacyNameToFieldKey =
        HistoriqueFilterService.LegacyFieldName.ToDictionary(
            kv => kv.Value.ToUpperInvariant(),
            kv => kv.Key);
}
