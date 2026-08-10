using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class Historique
{
    public long IdHisto { get; set; }

    public long NumClient { get; set; }

    public DateOnly? DateSaisie { get; set; }

    public TimeOnly? HeureSaisie { get; set; }

    public string? AssistanteCommercial { get; set; }

    public DateOnly? DateRappel { get; set; }

    public TimeOnly? HeureRappel { get; set; }

    public string? Operation { get; set; }

    public string? StatusVente { get; set; }

    public string? Note { get; set; }

    public string? Franchise { get; set; }

    public string? RaisonSociale { get; set; }

    public string? Cp { get; set; }

    public string? Ville { get; set; }

    public string? StatutsClients { get; set; }

    public string? MagasinPrincipal { get; set; }

    public byte[]? FicStk { get; set; }

    public virtual FranceOptique NumClientNavigation { get; set; } = null!;
}
