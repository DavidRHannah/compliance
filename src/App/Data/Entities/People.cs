namespace App.Data.Entities;

public class User
{
    public Guid Id { get; set; }

    /// <summary>Cognito subject, set when the user is invited.</summary>
    public required string ExternalId { get; set; }

    /// <summary>Unique ignoring case.</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>No default; every invite names the role.</summary>
    public UserRole Role { get; set; }

    /// <summary>Set for client users only.</summary>
    public Guid? ClientId { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Client
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
