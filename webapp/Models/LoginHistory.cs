using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("login_history")]
[Index("LoggedInAt", Name = "idx_login_history_logged_in_at")]
[Index("Login", Name = "idx_login_history_login")]
public partial class LoginHistory
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("login")]
    [StringLength(100)]
    public string Login { get; set; } = null!;

    [Column("logged_in_at", TypeName = "timestamp without time zone")]
    public DateTime LoggedInAt { get; set; }

    [ForeignKey("Login")]
    [InverseProperty("LoginHistories")]
    public virtual User LoginNavigation { get; set; } = null!;
}
