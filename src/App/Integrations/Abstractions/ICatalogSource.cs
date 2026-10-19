namespace App.Integrations.Abstractions;

/// <summary>Where the import-catalog task reads the licensed catalog CSV from.</summary>
public interface ICatalogSource
{
    Task<TextReader> OpenAsync(CancellationToken ct);
}
