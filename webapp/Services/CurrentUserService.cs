using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MafaliCrm.Web.Services;

// Wraps AuthenticationStateProvider so pages don't each repeat the same
// claim-lookup logic. Role names ("admin"/"assistant"/"commercial") come
// straight from the auth cookie's claims — no DB round-trip — matching
// users.role's CHECK constraint (database/05_users_and_roles.sql).
public class CurrentUserService
{
    private readonly AuthenticationStateProvider _authStateProvider;

    public CurrentUserService(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    private async Task<ClaimsPrincipal> GetUserAsync()
    {
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        return state.User;
    }

    public async Task<string?> GetLoginAsync() => (await GetUserAsync()).Identity?.Name;

    public async Task<string?> GetRoleAsync() => (await GetUserAsync()).FindFirst(ClaimTypes.Role)?.Value;

    // Admin and Assistant both see every saved filter; Commercial sees only
    // their own — per Badr's 2026-08-14 spec (see PROGRESS.md).
    public async Task<bool> CanViewAllFiltersAsync() => await GetRoleAsync() is "admin" or "assistant";

    // Only Admin can create a filter (for themselves or anyone else) —
    // Assistant and Commercial can't create at all.
    public async Task<bool> CanCreateFiltersAsync() => await GetRoleAsync() == "admin";

    // User administration (create/edit accounts, roles, password resets,
    // login history) is Admin-only — this is a page-level gate, backed by
    // [Authorize(Roles = "admin")] on the pages themselves for the real
    // enforcement, not just a hidden button.
    public async Task<bool> IsAdminAsync() => await GetRoleAsync() == "admin";
}
