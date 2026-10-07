namespace App.Tests;

/// <summary>
/// Declares the requirement a test proves. The requirements page is generated from these.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirementAttribute(string id, string text) : Attribute
{
    public string Id { get; } = id;
    public string Text { get; } = text;
}
