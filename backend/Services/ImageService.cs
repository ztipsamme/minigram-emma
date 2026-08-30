using System.Net.Mime;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace backend.Services
{
    public class ImageService
    {
        private readonly BlobServiceClient _serviceClient;
        private readonly BlobContainerClient _container;
        private UserDelegationKey? _cachedKey;
        private DateTimeOffset _cachedKeyExpires = DateTimeOffset.MinValue;
        private readonly SemaphoreSlim _keyLock = new(1, 1);

        private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/gif", "image/webp"
        };
        public ImageService(IConfiguration config)
        {
            var accountURL = config["Storage:AccountUrl"]!;
            var containerName = config["Storage:Container"] ?? "bilder";

            _serviceClient = new BlobServiceClient(
                new Uri(accountURL),
                new DefaultAzureCredential());

            _container = _serviceClient.GetBlobContainerClient(containerName);
        }

        public async Task<List<ImageDTO>> GetAllAsync()
        {
            List<ImageDTO> res = new();
            var delegationKey = await GetUserDelegationKeyAsync();

            await foreach (BlobItem blobItem in _container.GetBlobsAsync(BlobTraits.Metadata))
            {
                var dto = MapToImageDTO(blobItem.Name, blobItem.Metadata, delegationKey);
                res.Add(dto);
            }

            return res;
        }

        public async Task<ImageDTO?> GetByIdAsync(string id)
        {
            var delegationKey = await GetUserDelegationKeyAsync();

            await foreach (BlobItem blobItem in _container.GetBlobsAsync(BlobTraits.Metadata))
            {
                if (blobItem.Metadata.TryGetValue("Id", out var blobId) && blobId == id)
                {
                    return MapToImageDTO(blobItem.Name, blobItem.Metadata, delegationKey);
                }
            }

            return null;
        }

        public async Task<ImageDTO> CreateImageAsync(
            string fileName,
            Stream content,
            string contentType,
            string? caption,
            List<string>? tags)
        {
            if (!AllowedContentTypes.Contains(contentType))
                throw new ArgumentException($"Otillåten filtyp: {contentType}");
 
            var id = Guid.NewGuid().ToString();
            var extension = Path.GetExtension(fileName);
            var blobName = $"{id}{extension}";
 
            var blob = _container.GetBlobClient(blobName);
 
            var metadata = new Dictionary<string, string>
            {
                ["Id"] = id,
                ["Caption"] = EncodeValue(caption),
                ["Tags"] = EncodeTags(tags),
                ["OriginalName"] = EncodeValue(fileName)
            };
 
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = metadata
            });
 
            var delegationKey = await GetUserDelegationKeyAsync();
            return MapToImageDTO(blobName, metadata, delegationKey);
        }

        public async Task<ImageDTO?> UpdateImageAsync(string id, ImageUpdate imageUpdate)
        {
            var blobItem = await FindBlobByIdAsync(id);
            if (blobItem is null) return null;
 
            var blob = _container.GetBlobClient(blobItem.Name);
 
            // Läs befintlig metadata så vi inte råkar radera fält vi inte rör.
            var properties = await blob.GetPropertiesAsync();
            var metadata = new Dictionary<string, string>(properties.Value.Metadata);
 
            if (!string.IsNullOrWhiteSpace(imageUpdate.Caption))
                metadata["Caption"] = EncodeValue(imageUpdate.Caption);
 
            if (imageUpdate.Tags is { Count: > 0 })
                metadata["Tags"] = EncodeTags(imageUpdate.Tags);
 
            await blob.SetMetadataAsync(metadata);
 
            var delegationKey = await GetUserDelegationKeyAsync();
            return MapToImageDTO(blobItem.Name, metadata, delegationKey);
        }

        public async Task<bool> DeleteImageByIdAsync(string id)
        {
            var blobItem = await FindBlobByIdAsync(id);
            if (blobItem is null) return false;
 
            var blob = _container.GetBlobClient(blobItem.Name);
 
            var response = await blob.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots);
 
            return response.Value;
        }

        private async Task<UserDelegationKey> GetUserDelegationKeyAsync()
        {
            if (_cachedKey is not null && DateTimeOffset.UtcNow < _cachedKeyExpires)
                return _cachedKey;
 
            await _keyLock.WaitAsync();
            try
            {
                if (_cachedKey is not null && DateTimeOffset.UtcNow < _cachedKeyExpires)
                    return _cachedKey;
 
                var key = await _serviceClient.GetUserDelegationKeyAsync(
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddHours(1));
 
                _cachedKey = key.Value;
                // Förnya i god tid innan nyckeln faktiskt går ut.
                _cachedKeyExpires = DateTimeOffset.UtcNow.AddMinutes(45);
                return _cachedKey;
            }
            finally
            {
                _keyLock.Release();
            }
        }

        private ImageDTO MapToImageDTO(
            string blobName,
            IDictionary<string, string> metadata,
            UserDelegationKey delegationKey)
        {
            var id = ReadMetadata(metadata, "Id");
            var caption = DecodeValue(ReadMetadata(metadata, "Caption"));
            var tags = DecodeTags(ReadMetadata(metadata, "Tags"));

    return new ImageDTO(
        Id: id,
        Name: blobName,
        Caption: caption,
        Tags: tags,
        Url: GenerateSasUrl(blobName, delegationKey)
    );
}

        private string GenerateSasUrl(string blobName, UserDelegationKey delegationKey)
        {
            var blobClient = _container.GetBlobClient(blobName);

            var sasBuilder = new Azure.Storage.Sas.BlobSasBuilder
            {
                BlobContainerName = _container.Name,
                BlobName = blobName,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            sasBuilder.SetPermissions(Azure.Storage.Sas.BlobSasPermissions.Read);

            var sasToken = sasBuilder.ToSasQueryParameters(
                delegationKey,
                _container.AccountName
            ).ToString();

            return $"{blobClient.Uri}?{sasToken}";
        }




        private async Task<BlobItem?> FindBlobByIdAsync(string id)
        {
            await foreach (BlobItem blobItem in _container.GetBlobsAsync(BlobTraits.Metadata))
            {
                if (ReadMetadata(blobItem.Metadata, "Id") == id)
                    return blobItem;
            }
 
            return null;
        }
        private static string? ReadMetadata(IDictionary<string, string> metadata, string key)
        {
            foreach (var kv in metadata)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
 
            return null;
        }
        private static string EncodeValue(string? value) =>
            string.IsNullOrEmpty(value) ? string.Empty : Uri.EscapeDataString(value);
 
        private static string DecodeValue(string? value) =>
            string.IsNullOrEmpty(value) ? string.Empty : Uri.UnescapeDataString(value);
 
        // Varje tagg kodas för sig, annars blir kommatecknet %2C och splitten går sönder.
        private static string EncodeTags(List<string>? tags) =>
            tags is null or { Count: 0 }
                ? string.Empty
                : string.Join(",", tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => Uri.EscapeDataString(t.Trim())));
 
        private static List<string> DecodeTags(string? raw) =>
            string.IsNullOrEmpty(raw)
                ? new List<string>()
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(Uri.UnescapeDataString)
                     .ToList();
    }
}