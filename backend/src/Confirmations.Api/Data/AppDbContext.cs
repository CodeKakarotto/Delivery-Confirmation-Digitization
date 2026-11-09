using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Data;

/// <summary>
/// The app's SQL Server database. For now it holds only the login tables
/// (users, roles, hashed passwords). Confirmation tables come in Step 3.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
}
