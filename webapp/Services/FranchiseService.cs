using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MafaliCrm.Web.Services;

public class FranchiseService
{
    private readonly AppDbContext _db;

    public FranchiseService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<string>> ListAsync()
    {
        return await _db.TypeFranchises
            .OrderBy(f => f.NomFranchise)
            .Select(f => f.NomFranchise)
            .ToListAsync();
    }

    public async Task CreateAsync(string nomFranchise)
    {
        _db.TypeFranchises.Add(new TypeFranchise { NomFranchise = nomFranchise });
        await SaveOrThrowFriendly();
    }

    // NomFranchise is referenced by FOUR tracked relationships (franchise,
    // franchise2, franchise3, franchise4 on FranceOptique), not just one like
    // TypeFamille — same restriction applies, ExecuteUpdateAsync from the
    // start this time instead of hitting the change-tracker error again.
    public async Task RenameAsync(string currentName, string newName)
    {
        int affected;
        try
        {
            affected = await _db.TypeFranchises
                .Where(f => f.NomFranchise == currentName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.NomFranchise, newName));
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette franchise existe déjà.");
        }

        if (affected == 0)
        {
            throw new InvalidOperationException("Franchise introuvable.");
        }
    }

    public async Task DeleteAsync(string nomFranchise)
    {
        var entity = await _db.TypeFranchises.FindAsync(nomFranchise);
        if (entity is null) return;
        _db.TypeFranchises.Remove(entity);
        await _db.SaveChangesAsync();
    }

    private async Task SaveOrThrowFriendly()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette franchise existe déjà.");
        }
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is PostgresException { SqlState: "23505" } ||
        ex.InnerException is PostgresException { SqlState: "23505" };
}
