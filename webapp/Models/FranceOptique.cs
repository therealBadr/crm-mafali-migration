using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("france_optique")]
[Index("DateRappel", "HeureRappel", Name = "idx_france_optique_date_heure_rappel")]
[Index("Famille", Name = "idx_france_optique_famille")]
[Index("Franchise", Name = "idx_france_optique_franchise")]
[Index("Pays", Name = "idx_france_optique_pays")]
public partial class FranceOptique
{
    [Key]
    [Column("cle_opl")]
    public long CleOpl { get; set; }

    [Column("famille")]
    [StringLength(30)]
    public string? Famille { get; set; }

    [Column("raison_sociale")]
    [StringLength(60)]
    public string? RaisonSociale { get; set; }

    [Column("complement")]
    [StringLength(60)]
    public string? Complement { get; set; }

    [Column("franchise")]
    [StringLength(50)]
    public string? Franchise { get; set; }

    [Column("franchise2")]
    [StringLength(50)]
    public string? Franchise2 { get; set; }

    [Column("franchise3")]
    [StringLength(50)]
    public string? Franchise3 { get; set; }

    [Column("franchise4")]
    [StringLength(50)]
    public string? Franchise4 { get; set; }

    [Column("rue")]
    [StringLength(60)]
    public string? Rue { get; set; }

    [Column("localisation_1")]
    [StringLength(60)]
    public string? Localisation1 { get; set; }

    [Column("localisation_2")]
    [StringLength(60)]
    public string? Localisation2 { get; set; }

    [Column("cp")]
    [StringLength(5)]
    public string? Cp { get; set; }

    [Column("ville")]
    [StringLength(40)]
    public string? Ville { get; set; }

    [Column("pays")]
    [StringLength(30)]
    public string? Pays { get; set; }

    [Column("telephone")]
    [StringLength(20)]
    public string? Telephone { get; set; }

    [Column("tel_bis")]
    [StringLength(60)]
    public string? TelBis { get; set; }

    [Column("portable")]
    [StringLength(20)]
    public string? Portable { get; set; }

    [Column("fax")]
    [StringLength(20)]
    public string? Fax { get; set; }

    [Column("email")]
    [StringLength(50)]
    public string? Email { get; set; }

    [Column("assistante_commercial")]
    [StringLength(50)]
    public string? AssistanteCommercial { get; set; }

    [Column("responsable_achat")]
    [StringLength(50)]
    public string? ResponsableAchat { get; set; }

    [Column("representant")]
    [StringLength(100)]
    public string? Representant { get; set; }

    [Column("op_en_cours")]
    [StringLength(50)]
    public string? OpEnCours { get; set; }

    [Column("status_vente")]
    [StringLength(50)]
    public string? StatusVente { get; set; }

    [Column("statuts_clients")]
    [StringLength(50)]
    public string? StatutsClients { get; set; }

    [Column("etat_client")]
    [StringLength(50)]
    public string? EtatClient { get; set; }

    [Column("production")]
    [StringLength(50)]
    public string? Production { get; set; }

    [Column("magasin_principal")]
    [StringLength(50)]
    public string? MagasinPrincipal { get; set; }

    [Column("date_saisie")]
    public DateOnly? DateSaisie { get; set; }

    [Column("heure_saisie")]
    public TimeOnly? HeureSaisie { get; set; }

    [Column("date_rappel")]
    public DateOnly? DateRappel { get; set; }

    [Column("heure_rappel")]
    public TimeOnly? HeureRappel { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("note_perm")]
    public string? NotePerm { get; set; }

    [Column("bloque")]
    public bool Bloque { get; set; }

    [Column("rappel_rdv")]
    public bool RappelRdv { get; set; }

    [Column("siret")]
    [StringLength(14)]
    public string? Siret { get; set; }

    [Column("siren")]
    [StringLength(9)]
    public string? Siren { get; set; }

    [Column("facturation_electronique")]
    public bool FacturationElectronique { get; set; }

    [Column("tva")]
    [StringLength(20)]
    public string? Tva { get; set; }

    // Validation workflow (database/19_france_optique_validation.sql).
    // 'pending' and 'validated' behave identically everywhere — the
    // difference is accountability, not behavior — so no read path
    // distinguishes them. 'refused' is the only status that changes
    // anything: it's hidden from every screen, the same role
    // Historique.DeletedAt plays, which is why reads filter
    // `!= "refused"` rather than `== "validated"`.
    [Column("validation_status")]
    [StringLength(20)]
    public string ValidationStatus { get; set; } = null!;

    // NULL for the 93,235 migrated legacy clients — nobody added those
    // through this app, and attributing them to someone would be inventing
    // data. Only set for clients created in the new app.
    [Column("added_by")]
    [StringLength(100)]
    public string? AddedBy { get; set; }

    // Also NULL on the legacy rows even though they're 'validated': their
    // status means "not subject to this workflow", and the NULL reviewer is
    // exactly what distinguishes that from a real human validation.
    [Column("validated_by")]
    [StringLength(100)]
    public string? ValidatedBy { get; set; }

    [Column("validated_at", TypeName = "timestamp without time zone")]
    public DateTime? ValidatedAt { get; set; }

    [Column("refusal_reason")]
    [StringLength(250)]
    public string? RefusalReason { get; set; }

    [InverseProperty("CleOplNavigation")]
    public virtual ICollection<Ca> Cas { get; set; } = new List<Ca>();

    [ForeignKey("Famille")]
    [InverseProperty("FranceOptiques")]
    public virtual TypeFamille? FamilleNavigation { get; set; }

    [ForeignKey("Franchise2")]
    [InverseProperty("FranceOptiqueFranchise2Navigations")]
    public virtual TypeFranchise? Franchise2Navigation { get; set; }

    [ForeignKey("Franchise3")]
    [InverseProperty("FranceOptiqueFranchise3Navigations")]
    public virtual TypeFranchise? Franchise3Navigation { get; set; }

    [ForeignKey("Franchise4")]
    [InverseProperty("FranceOptiqueFranchise4Navigations")]
    public virtual TypeFranchise? Franchise4Navigation { get; set; }

    [ForeignKey("Franchise")]
    [InverseProperty("FranceOptiqueFranchiseNavigations")]
    public virtual TypeFranchise? FranchiseNavigation { get; set; }

    [InverseProperty("NumClientNavigation")]
    public virtual ICollection<Historique> Historiques { get; set; } = new List<Historique>();

    [ForeignKey("Pays")]
    [InverseProperty("FranceOptiques")]
    public virtual Pays? PaysNavigation { get; set; }
}
