using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[PrimaryKey("CleOpl", "Annee")]
[Table("ca")]
public partial class Ca
{
    [Column("id_ca")]
    public long IdCa { get; set; }

    [Key]
    [Column("cle_opl")]
    public long CleOpl { get; set; }

    [Key]
    [Column("annee")]
    public int Annee { get; set; }

    [Column("ca")]
    public int? Ca1 { get; set; }

    [ForeignKey("CleOpl")]
    [InverseProperty("Cas")]
    public virtual FranceOptique CleOplNavigation { get; set; } = null!;
}
