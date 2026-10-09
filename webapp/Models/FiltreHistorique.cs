using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[PrimaryKey("NomOperateur", "NomFiltre")]
[Table("filtre_historique")]
public partial class FiltreHistorique
{
    [Key]
    [Column("nom_filtre")]
    [StringLength(50)]
    public string NomFiltre { get; set; } = null!;

    [Column("filtre_reel")]
    [StringLength(512)]
    public string? FiltreReel { get; set; }

    [Key]
    [Column("nom_operateur")]
    [StringLength(50)]
    public string NomOperateur { get; set; } = null!;
}
