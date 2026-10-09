using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace MafaliCrm.Web.Services;

// Step 2 of the "pop-up 2 minutes before a rappel RDV" feature (Badr,
// 2026-10-08): keeps ConnectedUserRegistry in step with the circuits that
// actually exist, so the background scanner can tell who is reachable.
//
// A CircuitHandler is how Blazor Server lets you observe the lifetime of a
// user's live connection. The framework resolves it from DI as Scoped —
// one instance per circuit — and calls these four methods on it, so each
// instance only ever deals with its own circuit and can hold its id in a
// field. Registered in Program.cs against the CircuitHandler base type,
// which is where the framework looks.
//
// Lifecycle, as the framework drives it:
//   OnCircuitOpenedAsync   the circuit exists (user has the app open)
//   OnConnectionUpAsync    SignalR is connected — also fires on reconnect
//   OnConnectionDownAsync  connection lost, circuit kept alive a while
//   OnCircuitClosedAsync   circuit abandoned for good
//
// The one real trap here is reading the login. These four methods are
// awaited by the framework while it brings the circuit up, and the
// authentication state is published separately: Blazor's
// ServerAuthenticationStateProvider only has a value once the framework
// calls SetAuthenticationState on it, and GetAuthenticationStateAsync
// *throws* if asked before that. So the login is never awaited inline here —
// doing so would tie circuit startup to an ordering we don't control.
// Instead both orderings are covered:
//
//   * subscribe to AuthenticationStateChanged, which SetAuthenticationState
//     raises — this is the path that delivers the login in the normal case,
//     and also catches a sign-out during a circuit's life;
//   * plus one immediate opportunistic read, in case the state was already
//     published before this handler ran, which throws harmlessly if not.
//
// Neither is awaited by the lifecycle methods, so a circuit never waits on
// attribution. The cost is that a login can land a moment after its circuit
// does; a rappel for someone in that sub-second gap simply isn't delivered
// on that tick, which is why this is the right trade rather than a problem.
public sealed class ConnectedUserCircuitHandler : CircuitHandler, IDisposable
{
    private readonly ConnectedUserRegistry _registry;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<ConnectedUserCircuitHandler> _logger;

    private string? _circuitId;
    private bool _subscribed;

    // Attribution deliberately has more than one path into it (the
    // opportunistic read, OnConnectionUpAsync's repair read, and the
    // AuthenticationStateChanged event), so the same login routinely
    // resolves two or three times for one circuit. Confirmed live on
    // 2026-10-08: the log showed each circuit attributed twice. Writing it
    // to the registry repeatedly is harmless and idempotent, but logging it
    // repeatedly at Information level is noise in production, so the last
    // applied value is remembered and only real changes are reported.
    private string? _lastLogin;
    private bool _everApplied;
    private readonly object _applyGate = new();

    public ConnectedUserCircuitHandler(
        ConnectedUserRegistry registry,
        AuthenticationStateProvider authStateProvider,
        ILogger<ConnectedUserCircuitHandler> logger)
    {
        _registry = registry;
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuitId = circuit.Id;
        _registry.CircuitOpened(circuit.Id);

        _authStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _subscribed = true;

        _logger.LogDebug("Circuit {CircuitId} opened (provider {Provider}); {Count} circuit(s) tracked",
            circuit.Id, _authStateProvider.GetType().Name, _registry.CircuitCount);

        // Opportunistic, deliberately not awaited — see the class comment.
        TryReadLoginNow();

        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _registry.ConnectionUp(circuit.Id);
        // Covers the reconnect case: a circuit that came back after a drop
        // may have had its login resolved already, but re-reading is cheap
        // and repairs an entry that never got attributed the first time.
        TryReadLoginNow();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _registry.ConnectionDown(circuit.Id);
        _logger.LogDebug("Circuit {CircuitId} disconnected (kept alive pending reconnect)", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Unsubscribe();
        _registry.CircuitClosed(circuit.Id);
        _logger.LogInformation("Circuit {CircuitId} closed; {Count} circuit(s) still tracked",
            circuit.Id, _registry.CircuitCount);
        return Task.CompletedTask;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) => ApplyWhenReady(task);

    private void TryReadLoginNow()
    {
        try
        {
            ApplyWhenReady(_authStateProvider.GetAuthenticationStateAsync());
        }
        catch (InvalidOperationException)
        {
            // Expected when the authentication state hasn't been published
            // yet — the AuthenticationStateChanged subscription will deliver
            // it. Not a failure, so not logged above Debug.
            _logger.LogDebug("Circuit {CircuitId}: auth state not published yet, awaiting the change event", _circuitId);
        }
    }

    // Takes the Task the provider hands out rather than an AuthenticationState,
    // because that's the shape AuthenticationStateChanged delivers and it may
    // not have completed yet. Not awaited by any caller — attribution lands
    // whenever it lands.
    private void ApplyWhenReady(Task<AuthenticationState> stateTask)
    {
        _ = Apply();

        async Task Apply()
        {
            var circuitId = _circuitId;
            if (circuitId is null) return;

            try
            {
                var state = await stateTask;
                var login = state.User.Identity?.IsAuthenticated == true ? state.User.Identity.Name : null;

                _registry.SetLogin(circuitId, login);

                bool changed;
                lock (_applyGate)
                {
                    changed = !_everApplied || !string.Equals(_lastLogin, login, StringComparison.Ordinal);
                    _lastLogin = login;
                    _everApplied = true;
                }

                // A re-resolution landing on the same login says nothing new.
                // A genuine change (sign-out mid-circuit) still reports.
                if (!changed) return;

                if (login is null)
                {
                    _logger.LogDebug("Circuit {CircuitId} is anonymous", circuitId);
                }
                else
                {
                    _logger.LogInformation("Circuit {CircuitId} attributed to login {Login}; connected logins now: {Logins}",
                        circuitId, login, string.Join(", ", _registry.ConnectedLogins()));
                }
            }
            catch (Exception ex)
            {
                // Never let attribution take down a circuit: a user whose
                // login couldn't be read simply gets no pop-ups, which is far
                // better than a broken session.
                _logger.LogWarning(ex, "Circuit {CircuitId}: could not resolve the login for rappel pop-ups", circuitId);
            }
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _authStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _subscribed = false;
    }

    // The framework disposes the circuit's DI scope, and this handler with
    // it. Unsubscribing here as well as in OnCircuitClosedAsync covers a
    // scope torn down without a clean close.
    public void Dispose()
    {
        Unsubscribe();
        if (_circuitId is not null) _registry.CircuitClosed(_circuitId);
    }
}
