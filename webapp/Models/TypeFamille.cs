using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class TypeFamille
{
    public string NomFamille { get; set; } = null!;

    public virtual ICollection<FranceOptique> FranceOptiques { get; set; } = new List<FranceOptique>();
}
