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

        public async Task<Image> CreateImageAsync(NewImage newImage)
        {
            throw new NotImplementedException();
        }

        public async Task<Image> UpdateImageAsync(string id, ImageUpdate imageUpdate)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> DeleteImageByIdAsync(string id)
        {
            throw new NotImplementedException();
        }

        private async Task<UserDelegationKey> GetUserDelegationKeyAsync()
        {
            return await _serviceClient
                .GetUserDelegationKeyAsync(
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddHours(1));
        }

        private ImageDTO MapToImageDTO(string blobName, IDictionary<string, string> metadata, UserDelegationKey delegationKey)
        {
            metadata.TryGetValue("Id", out var id);
            metadata.TryGetValue("Caption", out var caption);

            var tags = metadata.TryGetValue("Tags", out var t)
                ? t.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>();

            var sasUri = GenerateSasUrl(blobName, delegationKey);

            return new ImageDTO(
                    Id: id,
                    Name: blobName,
                    Caption: caption ?? string.Empty,
                    Tags: tags,
                    Url: sasUri.ToString()
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
    }
}