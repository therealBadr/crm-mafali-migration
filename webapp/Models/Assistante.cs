using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("assistantes")]
public partial class Assistante
{
    [Key]
    [Column("prenom_nom")]
    [StringLength(100)]
    public string PrenomNom { get; set; } = null!;

    [Column("service")]
    [StringLength(30)]
    public string? Service { get; set; }

    /// <summary>
    /// Deprecated 2026-08-14: superseded by users.role (admin/assistant see all, commercial sees own only). Kept until the app is fully switched over to reading users.role.
    /// </summary>
    [Column("all_filtres")]
    public bool AllFiltres { get; set; }

    [Column("commentaire")]
    [StringLength(50)]
    public string? Commentaire { get; set; }
}
