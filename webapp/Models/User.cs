using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Models;

[Table("users")]
public partial class User
{
    [Key]
    [Column("login")]
    [StringLength(100)]
    public string Login { get; set; } = null!;

    [Column("password_hash")]
    [StringLength(255)]
    public string PasswordHash { get; set; } = null!;

    [Column("role")]
    [StringLength(20)]
    public string Role { get; set; } = null!;

    [Column("must_change_password")]
    public bool MustChangePassword { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("last_login_at", TypeName = "timestamp without time zone")]
    public DateTime? LastLoginAt { get; set; }

    [Column("created_at", TypeName = "timestamp without time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("failed_login_count")]
    public int FailedLoginCount { get; set; }

    [Column("lockout_until", TypeName = "timestamp without time zone")]
    public DateTime? LockoutUntil { get; set; }

    // No nav properties for france_optique.added_by / validated_by, even
    // though both are real FKs to users.login (migration 19). Nothing in the
    // app navigates from a user to the clients they added or validated — the
    // screens display the login string directly, the same way
    // france_optique.assistante_commercial has always been a plain string.
    // Postgres still enforces both FKs; EF simply doesn't need to model them.
    [InverseProperty("LoginNavigation")]
    public virtual ICollection<LoginHistory> LoginHistories { get; set; } = new List<LoginHistory>();
}
