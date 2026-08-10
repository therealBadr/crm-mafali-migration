using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class FiltreHistorique
{
    public string NomFiltre { get; set; } = null!;

    public string? FiltreReel { get; set; }
}
