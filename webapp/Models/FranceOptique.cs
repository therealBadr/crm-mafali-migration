using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class FranceOptique
{
    public long CleOpl { get; set; }

    public string? Famille { get; set; }

    public string? RaisonSociale { get; set; }

    public string? Complement { get; set; }

    public string? Franchise { get; set; }

    public string? Franchise2 { get; set; }

    public string? Franchise3 { get; set; }

    public string? Franchise4 { get; set; }

    public string? Rue { get; set; }

    public string? Localisation1 { get; set; }

    public string? Localisation2 { get; set; }

    public string? Cp { get; set; }

    public string? Ville { get; set; }

    public string? Pays { get; set; }

    public string? Telephone { get; set; }

    public string? TelBis { get; set; }

    public string? Portable { get; set; }

    public string? Fax { get; set; }

    public string? Email { get; set; }

    public string? AssistanteCommercial { get; set; }

    public string? ResponsableAchat { get; set; }

    public string? Representant { get; set; }

    public string? OpEnCours { get; set; }

    public string? StatusVente { get; set; }

    public string? StatutsClients { get; set; }

    public string? EtatClient { get; set; }

    public string? Production { get; set; }

    public string? MagasinPrincipal { get; set; }

    public DateOnly? DateSaisie { get; set; }

    public TimeOnly? HeureSaisie { get; set; }

    public DateOnly? DateRappel { get; set; }

    public TimeOnly? HeureRappel { get; set; }

    public string? Note { get; set; }

    public string? NotePerm { get; set; }

    public bool Bloque { get; set; }

    public bool RappelRdv { get; set; }

    public virtual ICollection<Ca> Cas { get; set; } = new List<Ca>();

    public virtual TypeFamille? FamilleNavigation { get; set; }

    public virtual TypeFranchise? Franchise2Navigation { get; set; }

    public virtual TypeFranchise? Franchise3Navigation { get; set; }

    public virtual TypeFranchise? Franchise4Navigation { get; set; }

    public virtual TypeFranchise? FranchiseNavigation { get; set; }

    public virtual ICollection<Historique> Historiques { get; set; } = new List<Historique>();

    public virtual Pay? PaysNavigation { get; set; }
}
