using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class Pay
{
    public long IdPays { get; set; }

    public string NomPays { get; set; } = null!;

    public string? Indicatif { get; set; }

    public string? Masque { get; set; }

    public virtual ICollection<FranceOptique> FranceOptiques { get; set; } = new List<FranceOptique>();
}
