using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("historique")]
[Index("DeletedAt", Name = "idx_historique_deleted_at")]
[Index("NumClient", Name = "idx_historique_num_client")]
public partial class Historique
{
    [Key]
    [Column("id_histo")]
    public long IdHisto { get; set; }

    [Column("num_client")]
    public long NumClient { get; set; }

    [Column("date_saisie")]
    public DateOnly? DateSaisie { get; set; }

    [Column("heure_saisie")]
    public TimeOnly? HeureSaisie { get; set; }

    [Column("assistante_commercial")]
    [StringLength(50)]
    public string? AssistanteCommercial { get; set; }

    [Column("date_rappel")]
    public DateOnly? DateRappel { get; set; }

    [Column("heure_rappel")]
    public TimeOnly? HeureRappel { get; set; }

    [Column("operation")]
    [StringLength(50)]
    public string? Operation { get; set; }

    [Column("status_vente")]
    [StringLength(50)]
    public string? StatusVente { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("franchise")]
    [StringLength(50)]
    public string? Franchise { get; set; }

    [Column("raison_sociale")]
    [StringLength(60)]
    public string? RaisonSociale { get; set; }

    [Column("cp")]
    [StringLength(5)]
    public string? Cp { get; set; }

    [Column("ville")]
    [StringLength(40)]
    public string? Ville { get; set; }

    [Column("statuts_clients")]
    [StringLength(50)]
    public string? StatutsClients { get; set; }

    [Column("magasin_principal")]
    [StringLength(50)]
    public string? MagasinPrincipal { get; set; }

    [Column("fic_stk")]
    public byte[]? FicStk { get; set; }

    [Column("deleted_at", TypeName = "timestamp without time zone")]
    public DateTime? DeletedAt { get; set; }

    [Column("fic_stk_nom")]
    [StringLength(255)]
    public string? FicStkNom { get; set; }

    [ForeignKey("NumClient")]
    [InverseProperty("Historiques")]
    public virtual FranceOptique NumClientNavigation { get; set; } = null!;
}
