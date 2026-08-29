using System.Net.Mime;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace backend.Services
{
    public class ImageService
    {
        private readonly BlobContainerClient _container;
        private readonly bool _isDev;

        public ImageService(IConfiguration config, bool isDev)
        {
            var accountURL = config["Storage:AccountURL"]!;
            var containerName = config["Storage:Container"] ?? "bilder";

            var serviceClient = new BlobServiceClient(
                new Uri(accountURL),
                new DefaultAzureCredential());

            _container = serviceClient.GetBlobContainerClient(containerName);
            _isDev = isDev;
        }

        public async Task<List<Image>> GetAllAsync()
        {
            List<Image> res = new();

            await foreach (BlobItem blobItem in _container.GetBlobsAsync(BlobTraits.Metadata))
            {
                var metadata = blobItem.Metadata;

                metadata.TryGetValue("Caption", out var caption);
                metadata.TryGetValue("Tags", out var tagsRaw);

                var tags = string.IsNullOrWhiteSpace(tagsRaw)
                    ? new List<string>()
                    : tagsRaw.Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();

                var blobClient = _container.GetBlobClient(blobItem.Name);

                var sasUri = blobClient.GenerateSasUri(
                    Azure.Storage.Sas.BlobSasPermissions.Read,
                    DateTimeOffset.UtcNow.AddHours(1));

                res.Add(new Image(
                    Id: 1,
                    Name: blobItem.Name,
                    Caption: caption ?? string.Empty,
                    Tags: tags,
                    Url: sasUri.ToString()
                ));
            }

            return res;
        }

        public async Task<Image> GetByIdAsync(int id)
        {
            throw new NotImplementedException();
        }

        public async Task<Image> CreateImageAsync(NewImage newImage)
        {
            throw new NotImplementedException();
        }

        public async Task<Image> UpdateImageAsync(int id, ImageUpdate imageUpdate)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> DeleteImageByIdAsync(int id)
        {
            throw new NotImplementedException();
        }
    }
}