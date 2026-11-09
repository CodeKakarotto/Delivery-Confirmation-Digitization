namespace Confirmations.Api.Auth;

/// <summary>The three account types. Drivers exist only so they can be blocked.</summary>
public static class Roles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Driver = "Driver";

    public static readonly string[] All = [Employee, Manager, Driver];
}
