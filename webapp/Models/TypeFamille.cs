using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("type_famille")]
public partial class TypeFamille
{
    [Key]
    [Column("nom_famille")]
    [StringLength(30)]
    public string NomFamille { get; set; } = null!;

    [InverseProperty("FamilleNavigation")]
    public virtual ICollection<FranceOptique> FranceOptiques { get; set; } = new List<FranceOptique>();
}
