using System;
using System.Collections.Generic;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Assistante> Assistantes { get; set; }

    public virtual DbSet<Ca> Cas { get; set; }

    public virtual DbSet<FiltreHistorique> FiltreHistoriques { get; set; }

    public virtual DbSet<FiltreOperatrice> FiltreOperatrices { get; set; }

    public virtual DbSet<FranceOptique> FranceOptiques { get; set; }

    public virtual DbSet<Historique> Historiques { get; set; }

    public virtual DbSet<LoginHistory> LoginHistories { get; set; }

    public virtual DbSet<Pays> Pays { get; set; }

    public virtual DbSet<TypeFamille> TypeFamilles { get; set; }

    public virtual DbSet<TypeFranchise> TypeFranchises { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Assistante>(entity =>
        {
            entity.HasKey(e => e.PrenomNom).HasName("assistantes_pkey");

            entity.Property(e => e.AllFiltres)
                .HasDefaultValue(false)
                .HasComment("Deprecated 2026-08-14: superseded by users.role (admin/assistant see all, commercial sees own only). Kept until the app is fully switched over to reading users.role.");
        });

        modelBuilder.Entity<Ca>(entity =>
        {
            entity.HasKey(e => new { e.CleOpl, e.Annee }).HasName("ca_pkey");

            entity.Property(e => e.Annee).HasDefaultValue(0);
            entity.Property(e => e.Ca1).HasDefaultValue(0);
            entity.Property(e => e.IdCa)
                .ValueGeneratedOnAdd()
                .UseIdentityAlwaysColumn();

            entity.HasOne(d => d.CleOplNavigation).WithMany(p => p.Cas)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_ca_cle_opl");
        });

        modelBuilder.Entity<FiltreHistorique>(entity =>
        {
            entity.HasKey(e => new { e.NomOperateur, e.NomFiltre }).HasName("filtre_historique_pkey");
        });

        modelBuilder.Entity<FiltreOperatrice>(entity =>
        {
            entity.HasKey(e => new { e.NomOperateur, e.NomFiltre }).HasName("filtre_operatrice_pkey");
        });

        modelBuilder.Entity<FranceOptique>(entity =>
        {
            entity.HasKey(e => e.CleOpl).HasName("france_optique_pkey");

            entity.Property(e => e.CleOpl).ValueGeneratedNever();
            entity.Property(e => e.Bloque).HasDefaultValue(false);
            entity.Property(e => e.EtatClient).HasDefaultValueSql("'0'::character varying");
            entity.Property(e => e.FacturationElectronique).HasDefaultValue(false);
            entity.Property(e => e.RappelRdv).HasDefaultValue(false);
            entity.Property(e => e.StatutsClients).HasDefaultValueSql("'-1'::character varying");
            entity.Property(e => e.ValidationStatus).HasDefaultValueSql("'pending'::character varying");

            // Refused clients are hidden app-wide (migration 19). This is a
            // global query filter rather than a `!= "refused"` clause repeated
            // at each call site: EF Core silently appends it to EVERY query
            // against FranceOptiques — ClientService.ListAsync,
            // ClientFilterService's ~30 distinct-value queries, RappelService,
            // BonCommandeEnCoursService, the Excel export, and anything added
            // later — so it is structurally impossible to forget somewhere.
            //
            // Deliberately a different approach from historique.deleted_at,
            // which repeats `DeletedAt == null` across nine call sites. That
            // works, but it only works as long as everyone remembers; with
            // ~40 read sites against this table the odds of a future query
            // missing the filter are too high to rely on discipline.
            //
            // Two places need refused rows and opt out explicitly with
            // IgnoreQueryFilters(): ClientValidationService.ListByAdderAsync
            // (a Commercial must see that their client was refused, and why)
            // and GetIncludingRefusedAsync (the validation screen rendering an
            // already-decided client read-only). Note 'pending' is NOT
            // filtered — a Commercial's client is live and usable immediately,
            // which is the whole point of the 2026-10-05 design.
            //
            // Startup logs two warnings from this (EF 10622): FranceOptique is
            // the required end of relationships from Historique and Ca, and a
            // filter on the principal "may lead to unexpected results". Left
            // as-is deliberately, after checking what it actually means here:
            // the concern is a dependent row whose required principal is
            // filtered away. That can only happen for a refused client, and
            // HistoriqueService/CaService both query by num_client/cle_opl
            // without Include()-ing the client, so neither ever materializes
            // the navigation. Silencing it by making the navigations optional
            // would weaken a real schema constraint (both FKs are ON DELETE
            // RESTRICT) to quiet a log line, and adding matching filters to
            // Historique/Ca would hide a refused client's interaction history
            // from the audit trail — the opposite of what Risk Register #13
            // asks for.
            entity.HasQueryFilter(c => c.ValidationStatus != "refused");

            entity.HasOne(d => d.FamilleNavigation).WithMany(p => p.FranceOptiques)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_famille");

            entity.HasOne(d => d.FranchiseNavigation).WithMany(p => p.FranceOptiqueFranchiseNavigations)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_franchise");

            entity.HasOne(d => d.Franchise2Navigation).WithMany(p => p.FranceOptiqueFranchise2Navigations)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_franchise2");

            entity.HasOne(d => d.Franchise3Navigation).WithMany(p => p.FranceOptiqueFranchise3Navigations)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_franchise3");

            entity.HasOne(d => d.Franchise4Navigation).WithMany(p => p.FranceOptiqueFranchise4Navigations)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_franchise4");

            entity.HasOne(d => d.PaysNavigation).WithMany(p => p.FranceOptiques)
                .HasPrincipalKey(p => p.NomPays)
                .HasForeignKey(d => d.Pays)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_pays");
        });

        modelBuilder.Entity<Historique>(entity =>
        {
            entity.HasKey(e => e.IdHisto).HasName("historique_pkey");

            entity.Property(e => e.IdHisto).UseIdentityAlwaysColumn();
            entity.Property(e => e.StatutsClients).HasDefaultValueSql("'-1'::character varying");

            entity.HasOne(d => d.NumClientNavigation).WithMany(p => p.Historiques)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_historique_num_client");
        });

        modelBuilder.Entity<LoginHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("login_history_pkey");

            entity.Property(e => e.Id).UseIdentityAlwaysColumn();
            entity.Property(e => e.LoggedInAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.LoginNavigation).WithMany(p => p.LoginHistories)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("login_history_login_fkey");
        });

        modelBuilder.Entity<Pays>(entity =>
        {
            entity.HasKey(e => e.IdPays).HasName("pays_pkey");

            entity.Property(e => e.IdPays).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<TypeFamille>(entity =>
        {
            entity.HasKey(e => e.NomFamille).HasName("type_famille_pkey");
        });

        modelBuilder.Entity<TypeFranchise>(entity =>
        {
            entity.HasKey(e => e.NomFranchise).HasName("type_franchise_pkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Login).HasName("users_pkey");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.FailedLoginCount).HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.MustChangePassword).HasDefaultValue(true);
        });
        modelBuilder.HasSequence("bon_commande_number_seq");
        modelBuilder.HasSequence("france_optique_cle_opl_seq");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
