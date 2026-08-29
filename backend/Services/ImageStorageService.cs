using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

public class ImageStorageService
{
    private readonly BlobContainerClient _containerClient;

    public ImageStorageService(BlobContainerClient containerClient)
    {
        _containerClient = containerClient;
    }

    public BlobClient GetBlobClient(string blobName) =>
        _containerClient.GetBlobClient(blobName);

    public async Task<BlobClient?> FindImageBlob(int id)
    {
        await foreach (var blob in _containerClient.GetBlobsAsync())
        {
            var fileName = Path.GetFileNameWithoutExtension(blob.Name);

            if (fileName == id.ToString())
            {
                return _containerClient.GetBlobClient(blob.Name);
            }
        }

        return null;
    }

    public async Task<int> GetNextId()
    {
        var maxId = 0;

        await foreach (var blob in _containerClient.GetBlobsAsync(
            BlobTraits.None,
            BlobStates.None,
            null,
            default))
        {
            var fileName = Path.GetFileNameWithoutExtension(blob.Name);

            if (int.TryParse(fileName, out var id))
            {
                maxId = Math.Max(maxId, id);
            }
        }

        return maxId + 1;
    }

    public async IAsyncEnumerable<BlobItem> GetBlobsAsync()
    {
        await foreach (var blob in _containerClient.GetBlobsAsync(
            BlobTraits.Metadata,
            BlobStates.None,
            null,
            default))
        {
            yield return blob;
        }
    }

    public Image? CreateImageFromBlob(BlobItem blob)
    {
        var fileName = Path.GetFileNameWithoutExtension(blob.Name);

        if (!int.TryParse(fileName, out var id))
        {
            return null;
        }

        var metadata = blob.Metadata;

        var originalName = metadata.TryGetValue("originalName", out var name)
            ? name
            : blob.Name;

        var caption = metadata.TryGetValue("caption", out var captionValue)
            ? captionValue
            : "";

        var tags = metadata.TryGetValue("tags", out var tagsValue)
            ? JsonSerializer.Deserialize<List<string>>(tagsValue) ?? []
            : [];

        return new Image(
            id,
            originalName,
            caption,
            tags,
            $"/bilder/{id}/image");
    }

    public static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }
}
