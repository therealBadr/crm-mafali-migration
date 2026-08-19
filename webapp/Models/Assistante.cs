using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class Assistante
{
    public string PrenomNom { get; set; } = null!;

    public string? Service { get; set; }

    /// <summary>
    /// Deprecated 2026-08-14: superseded by users.role (admin/assistant see all, commercial sees own only). Kept until the app is fully switched over to reading users.role.
    /// </summary>
    public bool AllFiltres { get; set; }

    public string? Commentaire { get; set; }
}
