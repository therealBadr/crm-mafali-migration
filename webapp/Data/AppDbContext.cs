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

    public virtual DbSet<Pays> Pays { get; set; }

    public virtual DbSet<TypeFamille> TypeFamilles { get; set; }

    public virtual DbSet<TypeFranchise> TypeFranchises { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Assistante>(entity =>
        {
            entity.HasKey(e => e.PrenomNom).HasName("assistantes_pkey");

            entity.ToTable("assistantes");

            entity.Property(e => e.PrenomNom)
                .HasMaxLength(100)
                .HasColumnName("prenom_nom");
            entity.Property(e => e.AllFiltres)
                .HasDefaultValue(false)
                .HasColumnName("all_filtres");
            entity.Property(e => e.Commentaire)
                .HasMaxLength(50)
                .HasColumnName("commentaire");
            entity.Property(e => e.Service)
                .HasMaxLength(30)
                .HasColumnName("service");
        });

        modelBuilder.Entity<Ca>(entity =>
        {
            entity.HasKey(e => new { e.CleOpl, e.Annee }).HasName("ca_pkey");

            entity.ToTable("ca");

            entity.Property(e => e.CleOpl).HasColumnName("cle_opl");
            entity.Property(e => e.Annee)
                .HasDefaultValue(0)
                .HasColumnName("annee");
            entity.Property(e => e.Ca1)
                .HasDefaultValue(0)
                .HasColumnName("ca");
            entity.Property(e => e.IdCa)
                .ValueGeneratedOnAdd()
                .UseIdentityAlwaysColumn()
                .HasColumnName("id_ca");

            entity.HasOne(d => d.CleOplNavigation).WithMany(p => p.Cas)
                .HasForeignKey(d => d.CleOpl)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_ca_cle_opl");
        });

        modelBuilder.Entity<FiltreHistorique>(entity =>
        {
            entity.HasKey(e => e.NomFiltre).HasName("filtre_historique_pkey");

            entity.ToTable("filtre_historique");

            entity.Property(e => e.NomFiltre)
                .HasMaxLength(50)
                .HasColumnName("nom_filtre");
            entity.Property(e => e.FiltreReel)
                .HasMaxLength(512)
                .HasColumnName("filtre_reel");
        });

        modelBuilder.Entity<FiltreOperatrice>(entity =>
        {
            entity.HasKey(e => new { e.NomOperateur, e.NomFiltre }).HasName("filtre_operatrice_pkey");

            entity.ToTable("filtre_operatrice");

            entity.Property(e => e.NomOperateur)
                .HasMaxLength(50)
                .HasColumnName("nom_operateur");
            entity.Property(e => e.NomFiltre)
                .HasMaxLength(50)
                .HasColumnName("nom_filtre");
            entity.Property(e => e.FiltreReel)
                .HasMaxLength(512)
                .HasColumnName("filtre_reel");
        });

        modelBuilder.Entity<FranceOptique>(entity =>
        {
            entity.HasKey(e => e.CleOpl).HasName("france_optique_pkey");

            entity.ToTable("france_optique");

            entity.HasIndex(e => new { e.DateRappel, e.HeureRappel }, "idx_france_optique_date_heure_rappel");

            entity.HasIndex(e => e.Famille, "idx_france_optique_famille");

            entity.HasIndex(e => e.Franchise, "idx_france_optique_franchise");

            entity.HasIndex(e => e.Pays, "idx_france_optique_pays");

            entity.Property(e => e.CleOpl)
                .ValueGeneratedNever()
                .HasColumnName("cle_opl");
            entity.Property(e => e.AssistanteCommercial)
                .HasMaxLength(50)
                .HasColumnName("assistante_commercial");
            entity.Property(e => e.Bloque)
                .HasDefaultValue(false)
                .HasColumnName("bloque");
            entity.Property(e => e.Complement)
                .HasMaxLength(60)
                .HasColumnName("complement");
            entity.Property(e => e.Cp)
                .HasMaxLength(5)
                .HasColumnName("cp");
            entity.Property(e => e.DateRappel).HasColumnName("date_rappel");
            entity.Property(e => e.DateSaisie).HasColumnName("date_saisie");
            entity.Property(e => e.Email)
                .HasMaxLength(50)
                .HasColumnName("email");
            entity.Property(e => e.EtatClient)
                .HasMaxLength(50)
                .HasDefaultValueSql("'0'::character varying")
                .HasColumnName("etat_client");
            entity.Property(e => e.Famille)
                .HasMaxLength(30)
                .HasColumnName("famille");
            entity.Property(e => e.Fax)
                .HasMaxLength(20)
                .HasColumnName("fax");
            entity.Property(e => e.Franchise)
                .HasMaxLength(50)
                .HasColumnName("franchise");
            entity.Property(e => e.Franchise2)
                .HasMaxLength(50)
                .HasColumnName("franchise2");
            entity.Property(e => e.Franchise3)
                .HasMaxLength(50)
                .HasColumnName("franchise3");
            entity.Property(e => e.Franchise4)
                .HasMaxLength(50)
                .HasColumnName("franchise4");
            entity.Property(e => e.HeureRappel).HasColumnName("heure_rappel");
            entity.Property(e => e.HeureSaisie).HasColumnName("heure_saisie");
            entity.Property(e => e.Localisation1)
                .HasMaxLength(60)
                .HasColumnName("localisation_1");
            entity.Property(e => e.Localisation2)
                .HasMaxLength(60)
                .HasColumnName("localisation_2");
            entity.Property(e => e.MagasinPrincipal)
                .HasMaxLength(50)
                .HasColumnName("magasin_principal");
            entity.Property(e => e.Note)
                .HasMaxLength(250)
                .HasColumnName("note");
            entity.Property(e => e.NotePerm).HasColumnName("note_perm");
            entity.Property(e => e.OpEnCours)
                .HasMaxLength(50)
                .HasColumnName("op_en_cours");
            entity.Property(e => e.Pays)
                .HasMaxLength(30)
                .HasColumnName("pays");
            entity.Property(e => e.Portable)
                .HasMaxLength(20)
                .HasColumnName("portable");
            entity.Property(e => e.Production)
                .HasMaxLength(50)
                .HasColumnName("production");
            entity.Property(e => e.RaisonSociale)
                .HasMaxLength(60)
                .HasColumnName("raison_sociale");
            entity.Property(e => e.RappelRdv)
                .HasDefaultValue(false)
                .HasColumnName("rappel_rdv");
            entity.Property(e => e.Representant)
                .HasMaxLength(100)
                .HasColumnName("representant");
            entity.Property(e => e.ResponsableAchat)
                .HasMaxLength(50)
                .HasColumnName("responsable_achat");
            entity.Property(e => e.Rue)
                .HasMaxLength(60)
                .HasColumnName("rue");
            entity.Property(e => e.StatusVente)
                .HasMaxLength(50)
                .HasColumnName("status_vente");
            entity.Property(e => e.StatutsClients)
                .HasMaxLength(50)
                .HasDefaultValueSql("'0'::character varying")
                .HasColumnName("statuts_clients");
            entity.Property(e => e.TelBis)
                .HasMaxLength(20)
                .HasColumnName("tel_bis");
            entity.Property(e => e.Telephone)
                .HasMaxLength(20)
                .HasColumnName("telephone");
            entity.Property(e => e.Ville)
                .HasMaxLength(40)
                .HasColumnName("ville");

            entity.HasOne(d => d.FamilleNavigation).WithMany(p => p.FranceOptiques)
                .HasForeignKey(d => d.Famille)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_famille");

            entity.HasOne(d => d.FranchiseNavigation).WithMany(p => p.FranceOptiqueFranchiseNavigations)
                .HasForeignKey(d => d.Franchise)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_franchise");

            entity.HasOne(d => d.Franchise2Navigation).WithMany(p => p.FranceOptiqueFranchise2Navigations)
                .HasForeignKey(d => d.Franchise2)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_franchise2");

            entity.HasOne(d => d.Franchise3Navigation).WithMany(p => p.FranceOptiqueFranchise3Navigations)
                .HasForeignKey(d => d.Franchise3)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_france_optique_franchise3");

            entity.HasOne(d => d.Franchise4Navigation).WithMany(p => p.FranceOptiqueFranchise4Navigations)
                .HasForeignKey(d => d.Franchise4)
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

            entity.ToTable("historique");

            entity.HasIndex(e => e.NumClient, "idx_historique_num_client");

            entity.Property(e => e.IdHisto)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id_histo");
            entity.Property(e => e.AssistanteCommercial)
                .HasMaxLength(50)
                .HasColumnName("assistante_commercial");
            entity.Property(e => e.Cp)
                .HasMaxLength(5)
                .HasColumnName("cp");
            entity.Property(e => e.DateRappel).HasColumnName("date_rappel");
            entity.Property(e => e.DateSaisie).HasColumnName("date_saisie");
            entity.Property(e => e.FicStk).HasColumnName("fic_stk");
            entity.Property(e => e.Franchise)
                .HasMaxLength(50)
                .HasColumnName("franchise");
            entity.Property(e => e.HeureRappel).HasColumnName("heure_rappel");
            entity.Property(e => e.HeureSaisie).HasColumnName("heure_saisie");
            entity.Property(e => e.MagasinPrincipal)
                .HasMaxLength(50)
                .HasColumnName("magasin_principal");
            entity.Property(e => e.Note)
                .HasMaxLength(250)
                .HasColumnName("note");
            entity.Property(e => e.NumClient).HasColumnName("num_client");
            entity.Property(e => e.Operation)
                .HasMaxLength(50)
                .HasColumnName("operation");
            entity.Property(e => e.RaisonSociale)
                .HasMaxLength(60)
                .HasColumnName("raison_sociale");
            entity.Property(e => e.StatusVente)
                .HasMaxLength(50)
                .HasColumnName("status_vente");
            entity.Property(e => e.StatutsClients)
                .HasMaxLength(50)
                .HasDefaultValueSql("'0'::character varying")
                .HasColumnName("statuts_clients");
            entity.Property(e => e.Ville)
                .HasMaxLength(40)
                .HasColumnName("ville");

            entity.HasOne(d => d.NumClientNavigation).WithMany(p => p.Historiques)
                .HasForeignKey(d => d.NumClient)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_historique_num_client");
        });

        modelBuilder.Entity<Pays>(entity =>
        {
            entity.HasKey(e => e.IdPays).HasName("pays_pkey");

            entity.ToTable("pays");

            entity.HasIndex(e => e.NomPays, "pays_nom_pays_key").IsUnique();

            entity.Property(e => e.IdPays)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id_pays");
            entity.Property(e => e.Indicatif)
                .HasMaxLength(50)
                .HasColumnName("indicatif");
            entity.Property(e => e.Masque)
                .HasMaxLength(50)
                .HasColumnName("masque");
            entity.Property(e => e.NomPays)
                .HasMaxLength(50)
                .HasColumnName("nom_pays");
        });

        modelBuilder.Entity<TypeFamille>(entity =>
        {
            entity.HasKey(e => e.NomFamille).HasName("type_famille_pkey");

            entity.ToTable("type_famille");

            entity.Property(e => e.NomFamille)
                .HasMaxLength(30)
                .HasColumnName("nom_famille");
        });

        modelBuilder.Entity<TypeFranchise>(entity =>
        {
            entity.HasKey(e => e.NomFranchise).HasName("type_franchise_pkey");

            entity.ToTable("type_franchise");

            entity.Property(e => e.NomFranchise)
                .HasMaxLength(50)
                .HasColumnName("nom_franchise");
        });
        modelBuilder.HasSequence("france_optique_cle_opl_seq");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
