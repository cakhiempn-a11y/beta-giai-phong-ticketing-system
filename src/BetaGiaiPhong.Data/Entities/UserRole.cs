namespace BetaGiaiPhong.Data.Entities;

public sealed class UserRole
{
    public long UserId { get; set; }
    public int RoleId { get; set; }

    public AppUser? User { get; set; }
    public Role? Role { get; set; }
}
