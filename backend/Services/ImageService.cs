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

            var userDelegationKey = await _serviceClient
                .GetUserDelegationKeyAsync(
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddHours(1));

            await foreach (BlobItem blobItem in _container.GetBlobsAsync(traits: BlobTraits.Metadata | BlobTraits.Tags))
            {
                var metadata = blobItem.Metadata;

                metadata.TryGetValue("Id", out var id);
                metadata.TryGetValue("Caption", out var caption);

                var tags = blobItem.Tags != null
                            ? blobItem.Tags.Values.ToList()
                            : new List<string>();

                var blobClient = _container.GetBlobClient(blobItem.Name);

                var sasBuilder = new Azure.Storage.Sas.BlobSasBuilder
                {
                    BlobContainerName = _container.Name,
                    BlobName = blobItem.Name,
                    Resource = "b",
                    StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                    ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
                };
                sasBuilder.SetPermissions(Azure.Storage.Sas.BlobSasPermissions.Read);

                var sasToken = sasBuilder.ToSasQueryParameters(
                    userDelegationKey,
                    _container.AccountName
                ).ToString();

                var sasUri = $"{blobClient.Uri}?{sasToken}";

                res.Add(new ImageDTO(
                    Id: id,
                    Name: blobItem.Name,
                    Caption: caption ?? string.Empty,
                    Tags: tags,
                    Url: sasUri.ToString()
                ));
            }

            return res;
        }

        public async Task<Image> GetByIdAsync(string id)
        {
            throw new NotImplementedException();
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
    }
}