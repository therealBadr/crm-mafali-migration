using System.Collections.Concurrent;

namespace MafaliCrm.Web.Services;

// One live Blazor circuit (≈ one browser tab with the app open) and which
// user it belongs to.
//
// Login is nullable because a circuit exists before anyone knows who owns
// it: ConnectedUserCircuitHandler records the circuit the moment it opens,
// and the login arrives a moment later once the framework has published the
// authentication state (see that file for why it can't be read inline).
// A circuit that is never attributed stays null — an anonymous visitor on
// the login page, for instance.
//
// Connected is tracked separately from existence. Blazor keeps a circuit
// alive for a while after the SignalR connection drops, so it can be
// resumed if the user's network blips; during that gap the circuit is still
// here but can't be reached. Both facts are kept so the scanner can decide
// what to do about it rather than having the choice made for it here.
public sealed record ConnectedCircuit(
    string CircuitId,
    string? Login,
    bool Connected,
    DateTimeOffset OpenedAt,
    DateTimeOffset? DisconnectedAt);

// Step 2 of the "pop-up 2 minutes before a rappel RDV" feature (Badr,
// 2026-10-08): the answer to "who is actually connected right now".
//
// Why this has to exist at all: the scanner in step 3 runs on a background
// thread with no circuit and no HTTP request, so it cannot use
// CurrentUserService — that's registered Scoped (Program.cs), meaning one
// instance per circuit, resolvable only from inside a user's own session.
// A background thread asking "is Bruno connected?" has nowhere to look.
// This registry is that place: a Singleton both sides can reach, written by
// each circuit as it comes and goes, read by the scanner each tick.
//
// Deliberately EF-free and dependency-free — no DbContext, no entities,
// nothing async. Presence is in-memory only and intentionally so: it
// describes this process's live connections, so it's meaningless to persist
// and correct for it to vanish on restart.
//
// Logins are compared ignoring case AND surrounding whitespace, to agree
// with RappelService.OwnerKey — the paired definition on the SQL side,
// which is lower(btrim(...)). The two must stay in step: the pop-up decides
// who owns a rappel with the comparison here, while the Rappels screen
// decides it in SQL, and a pop-up for a reminder missing from that screen
// would be impossible to explain. Trimming is per Badr's 2026-10-08 call;
// see RappelService for the data that prompted it.
//
// The one place the two could in principle disagree is non-ASCII: Postgres
// lower() and .NET's OrdinalIgnoreCase both fold accented Latin the same
// way, so French names are fine, but they aren't guaranteed identical for
// every script. Not worth more machinery than this note for a staff list
// of first names.
//
// Still not matched, deliberately: word-count differences. "Bruno Bruno"
// does not reach login "Bruno".
public class ConnectedUserRegistry
{
    // Keyed by circuit id, not by login: one person legitimately has several
    // circuits at once (two tabs, or a tab left open on another machine),
    // and each comes and goes independently.
    //
    // "Which logins are connected" is therefore derived by scanning rather
    // than kept as a second dictionary. That's a deliberate trade: this is
    // an office-sized app — tens of concurrent circuits, not thousands — so
    // the scan is far cheaper than the risk of a second index drifting out
    // of agreement with the first one.
    private readonly ConcurrentDictionary<string, ConnectedCircuit> _circuits = new();

    // Called as soon as a circuit opens, before anyone knows whose it is.
    public void CircuitOpened(string circuitId)
    {
        var now = DateTimeOffset.Now;
        _circuits[circuitId] = new ConnectedCircuit(circuitId, null, Connected: true, OpenedAt: now, DisconnectedAt: null);
    }

    // Called once the authentication state for a circuit is known, and again
    // if it changes while the circuit lives (a sign-out without a reload).
    // A null login clears the attribution rather than removing the circuit —
    // the circuit is still open, it just no longer belongs to anyone.
    //
    // Does nothing for a circuit that has already closed: the late auth
    // callback this guards against would otherwise resurrect a dead circuit
    // as a permanent phantom entry, and a phantom "connected" user is
    // exactly how a pop-up ends up being delivered into nowhere.
    public void SetLogin(string circuitId, string? login)
    {
        _circuits.TryUpdate_IfPresent(circuitId, existing => existing with
        {
            Login = string.IsNullOrWhiteSpace(login) ? null : login.Trim()
        });
    }

    public void ConnectionUp(string circuitId)
        => _circuits.TryUpdate_IfPresent(circuitId, e => e with { Connected = true, DisconnectedAt = null });

    public void ConnectionDown(string circuitId)
        => _circuits.TryUpdate_IfPresent(circuitId, e => e with { Connected = false, DisconnectedAt = DateTimeOffset.Now });

    public void CircuitClosed(string circuitId) => _circuits.TryRemove(circuitId, out _);

    // True when this login has at least one circuit that can be reached
    // right now. Disconnected-but-alive circuits deliberately don't count.
    public bool IsConnected(string login)
        => !string.IsNullOrWhiteSpace(login)
           && _circuits.Values.Any(c => c.Connected && Matches(c.Login, login));

    // Every reachable circuit belonging to this login. The scanner needs the
    // circuits, not just a yes/no, because a user with two tabs open should
    // get the pop-up in both.
    public List<ConnectedCircuit> CircuitsFor(string login)
        => string.IsNullOrWhiteSpace(login)
            ? new List<ConnectedCircuit>()
            : _circuits.Values.Where(c => c.Connected && Matches(c.Login, login)).ToList();

    // The distinct logins reachable right now. Lets the scanner skip the
    // owner-matching work entirely when nobody is connected.
    public List<string> ConnectedLogins()
        => _circuits.Values
            .Where(c => c.Connected && c.Login is not null)
            .Select(c => c.Login!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Everything currently tracked, including unattributed and
    // disconnected-but-alive circuits — for logging and diagnostics, and so
    // step 3 can reconsider the disconnected case without changing this class.
    public List<ConnectedCircuit> Snapshot() => _circuits.Values.ToList();

    public int CircuitCount => _circuits.Count;

    // Trims both sides, not just the stored login: SetLogin already trimmed
    // what it holds, but the value being looked up is an
    // Assistante_Commercial straight out of the client row, and those carry
    // their own stray whitespace (" Bruno", "RANIA RANIA ").
    private static bool Matches(string? a, string? b)
        => a is not null && b is not null
           && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

internal static class ConcurrentDictionaryExtensions
{
    // Update in place only if the key is still present, retrying if another
    // thread changed the entry in between. Needed because ConcurrentDictionary
    // offers no "update if present" that takes the existing value — AddOrUpdate
    // would happily re-add a circuit that has just closed.
    public static void TryUpdate_IfPresent<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dict, TKey key, Func<TValue, TValue> update)
        where TKey : notnull
    {
        while (dict.TryGetValue(key, out var existing))
        {
            if (dict.TryUpdate(key, update(existing), existing)) return;
        }
    }
}
