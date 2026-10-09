using System.Collections.Concurrent;

namespace MafaliCrm.Web.Services;

// Step 1 of the "pop-up 2 minutes before a rappel RDV" feature (Badr,
// 2026-10-08): turning a rappel's stored Date_Rappel + Heure_Rappel into one
// absolute instant, and deciding whether that instant falls inside the
// warning window.
//
// Deliberately EF-free and static — no DbContext, no entities, nothing
// async. The background scanner that will call this (step 3) ticks every 30
// seconds on its own thread, where a wrong result is near-impossible to
// observe directly; the timezone math is the part most likely to be wrong,
// so it lives in its own file with no database dependency precisely so it
// can be compiled and checked standalone instead of only through the UI.
//
// Heure_Rappel is wall-clock time in the *client's own country*, not server
// time — the convention TimeInput already established for its "Heure par
// Pays" toggle (database/17_pays_fuseau_horaire.sql). So 9h00 on a French
// client is 09:00 Paris, a genuinely different instant from 09:00 in
// Casablanca, and "2 minutes before" has to mean 2 minutes before the former.
// Conversion uses the same GetUtcOffset + DateTimeOffset pair as
// TimeInput.ConvertHomeToCompare, so DST edges resolve identically in both
// places rather than becoming two slightly different readings of the same
// stored 9h00.
public static class RappelDueTime
{
    // Etats Unis (spans 6 real zones) and MONDE (not an actual country) are
    // deliberately NULL in pays.fuseau_horaire. Badr's call, 2026-10-08:
    // those fall back to Maroc rather than being skipped — a pop-up whose
    // time may be off for a US client beats a rappel that never fires at
    // all. Also covers a client with no Pays set, and an unrecognised zone
    // name, so no row can be silently dropped for want of a timezone.
    public const string FallbackTimezone = "Africa/Casablanca";

    // How far ahead of the moment the pop-up appears.
    public const int DefaultLeadMinutes = 2;

    // TimeZoneInfo.FindSystemTimeZoneById throws on an unknown id rather
    // than returning null, and this runs every 30 seconds over every
    // candidate row — so resolved zones (including the ones that fell back)
    // are cached to keep a bad zone string from paying the cost of a thrown
    // exception on every tick. Concurrent because the scanner thread and a
    // user's circuit could both land here at once.
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> ZoneCache = new();

    // An unknown or missing zone resolves to FallbackTimezone. If the
    // fallback itself can't be resolved, that's a broken tz database on the
    // host rather than a data problem, and it throws — step 3's tick wraps
    // this so a misconfigured host logs loudly instead of silently never
    // popping anything up.
    public static TimeZoneInfo ResolveTimezone(string? fuseauHoraire)
    {
        var id = string.IsNullOrWhiteSpace(fuseauHoraire) ? FallbackTimezone : fuseauHoraire;
        return ZoneCache.GetOrAdd(id, static key =>
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(key);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById(FallbackTimezone);
            }
        });
    }

    // date + time are read as wall-clock time in the client's country;
    // the returned DateTimeOffset is the single absolute instant that names.
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, string? fuseauHoraire)
    {
        var tz = ResolveTimezone(fuseauHoraire);
        var local = date.ToDateTime(time);
        var offset = tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    // True when the moment is still ahead of us and no more than
    // leadMinutes away.
    //
    // The "moment >= now" half is what implements Badr's 2026-10-08 call
    // that past moments never fire: the real data carries hundreds of
    // already-overdue reminders per busy assistant, and without this the
    // first start-up would pop every one of them at once. Overdue rappels
    // stay visible on the Rappels page; they just never become pop-ups.
    //
    // Note this stays true across several consecutive 30-second ticks (at
    // 2:00, 1:30, 1:00 and 0:30 before the moment). Suppressing the repeats
    // is step 3's job — it holds the "already notified" set — not this
    // function's; it answers only "is this rappel inside the window right
    // now".
    public static bool IsDueSoon(DateTimeOffset moment, DateTimeOffset now, int leadMinutes = DefaultLeadMinutes)
        => moment >= now && moment <= now.AddMinutes(leadMinutes);

    // Which Date_Rappel values could possibly hold a moment inside the
    // window, for narrowing the SQL query before any conversion happens.
    //
    // Date_Rappel is a plain date with no zone attached, so the same instant
    // can carry a local date one day either side of the UTC date (real UTC
    // offsets run from -12:00 to +14:00). One day of slack in both
    // directions is therefore always enough, and it keeps the candidate set
    // at tens of rows instead of scanning all 93k clients — the precise
    // decision is made per row in C# afterwards, so a slightly wide net here
    // costs nothing but cannot miss anything.
    public static (DateOnly From, DateOnly To) CandidateDateRange(DateTimeOffset now)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        return (utcDate.AddDays(-1), utcDate.AddDays(1));
    }
}
