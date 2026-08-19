using System;
using System.Collections.Generic;

namespace MafaliCrm.Web.Models;

public partial class LoginHistory
{
    public long Id { get; set; }

    public string Login { get; set; } = null!;

    public DateTime LoggedInAt { get; set; }

    public virtual User LoginNavigation { get; set; } = null!;
}
