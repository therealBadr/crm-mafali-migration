using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class TypeFranchise
{
    public string NomFranchise { get; set; } = null!;

    public virtual ICollection<FranceOptique> FranceOptiqueFranchise2Navigations { get; set; } = new List<FranceOptique>();

    public virtual ICollection<FranceOptique> FranceOptiqueFranchise3Navigations { get; set; } = new List<FranceOptique>();

    public virtual ICollection<FranceOptique> FranceOptiqueFranchise4Navigations { get; set; } = new List<FranceOptique>();

    public virtual ICollection<FranceOptique> FranceOptiqueFranchiseNavigations { get; set; } = new List<FranceOptique>();
}
