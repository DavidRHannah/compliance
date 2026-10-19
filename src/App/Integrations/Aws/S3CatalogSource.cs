using Amazon.S3;
using App.Integrations.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Integrations.Aws;

public sealed class CatalogSettings
{
    public string Bucket { get; set; } = "";
    public string Key { get; set; } = "";
}

public sealed class S3CatalogSource(IAmazonS3 s3, IOptions<CatalogSettings> settings) : ICatalogSource
{
    public async Task<TextReader> OpenAsync(CancellationToken ct)
    {
        var catalog = settings.Value;
        if (string.IsNullOrEmpty(catalog.Bucket) || string.IsNullOrEmpty(catalog.Key))
        {
            throw new InvalidOperationException("Set Catalog:Bucket and Catalog:Key, or pass --file <path>.");
        }

        var response = await s3.GetObjectAsync(catalog.Bucket, catalog.Key, ct);
        return new StreamReader(response.ResponseStream);
    }
}
