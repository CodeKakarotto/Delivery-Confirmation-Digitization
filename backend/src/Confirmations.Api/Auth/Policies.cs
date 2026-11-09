using Microsoft.AspNetCore.Authorization;

namespace Confirmations.Api.Auth;

/// <summary>
/// Named access rules for endpoints, e.g. [Authorize(Policy = Policies.ManagerOnly)].
/// </summary>
public static class Policies
{
    /// <summary>Employees and Managers. Drivers and logged-out users are refused.</summary>
    public const string EmployeeOrManager = "EmployeeOrManager";

    /// <summary>Managers only (resolve flagged records, void confirmations).</summary>
    public const string ManagerOnly = "ManagerOnly";

    public static AuthorizationPolicy BuildEmployeeOrManager() =>
        new AuthorizationPolicyBuilder().RequireRole(Roles.Employee, Roles.Manager).Build();

    public static void AddConfirmationPolicies(this IServiceCollection services)
    {
        var employeeOrManager = BuildEmployeeOrManager();

        services.AddAuthorizationBuilder()
            .AddPolicy(EmployeeOrManager, employeeOrManager)
            .AddPolicy(ManagerOnly, policy => policy.RequireRole(Roles.Manager))
            // Endpoints with a bare [Authorize] get this rule...
            .SetDefaultPolicy(employeeOrManager)
            // ...and endpoints with no rule at all get it too, so a forgotten
            // [Authorize] never exposes data to drivers or logged-out users.
            .SetFallbackPolicy(employeeOrManager);
    }
}
