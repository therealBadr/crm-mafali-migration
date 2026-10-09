using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("type_franchise")]
public partial class TypeFranchise
{
    [Key]
    [Column("nom_franchise")]
    [StringLength(50)]
    public string NomFranchise { get; set; } = null!;

    [InverseProperty("Franchise2Navigation")]
    public virtual ICollection<FranceOptique> FranceOptiqueFranchise2Navigations { get; set; } = new List<FranceOptique>();

    [InverseProperty("Franchise3Navigation")]
    public virtual ICollection<FranceOptique> FranceOptiqueFranchise3Navigations { get; set; } = new List<FranceOptique>();

    [InverseProperty("Franchise4Navigation")]
    public virtual ICollection<FranceOptique> FranceOptiqueFranchise4Navigations { get; set; } = new List<FranceOptique>();

    [InverseProperty("FranchiseNavigation")]
    public virtual ICollection<FranceOptique> FranceOptiqueFranchiseNavigations { get; set; } = new List<FranceOptique>();
}
