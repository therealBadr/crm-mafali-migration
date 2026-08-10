using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class Assistante
{
    public string PrenomNom { get; set; } = null!;

    public string? Service { get; set; }

    public bool AllFiltres { get; set; }

    public string? Commentaire { get; set; }
}
