using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("pays")]
[Index("NomPays", Name = "pays_nom_pays_key", IsUnique = true)]
public partial class Pays
{
    [Key]
    [Column("id_pays")]
    public long IdPays { get; set; }

    [Column("nom_pays")]
    [StringLength(50)]
    public string NomPays { get; set; } = null!;

    [Column("indicatif")]
    [StringLength(50)]
    public string? Indicatif { get; set; }

    [Column("masque")]
    [StringLength(50)]
    public string? Masque { get; set; }

    [Column("fuseau_horaire")]
    [StringLength(50)]
    public string? FuseauHoraire { get; set; }

    [InverseProperty("PaysNavigation")]
    public virtual ICollection<FranceOptique> FranceOptiques { get; set; } = new List<FranceOptique>();
}
