using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class Ca
{
    public long IdCa { get; set; }

    public long CleOpl { get; set; }

    public int Annee { get; set; }

    public int? Ca1 { get; set; }

    public virtual FranceOptique CleOplNavigation { get; set; } = null!;
}
