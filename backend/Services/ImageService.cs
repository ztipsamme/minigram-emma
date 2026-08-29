using System.Net.Mime;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace backend.Services
{
    public class ImageService
    {
        private readonly BlobContainerClient _container;

        public ImageService(IConfiguration config)
        {
            var accountURL = config["Storage:AccountURL"]!;
            var containerName = config["Storage:Container"] ?? "bilder";

            var serviceClient = new BlobServiceClient(
                new Uri(accountURL),
                new DefaultAzureCredential());

            _container = serviceClient.GetBlobContainerClient(containerName);
        }

        public async Task<string> UploadAsync(string blobName, Stream content, string contentType)
        {
            var blob = _container.GetBlobClient(blobName);

            await blob.UploadAsync(content, new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = contentType
            });

            return blob.Name;
        }

        public async Task<(Stream Content, string ContentType)?> DownloadAsync(string blobName)
        {
            var blob = _container.GetBlobClient(blobName);
            if (!await blob.ExistsAsync()) return null;

            var download = await blob.DownloadStreamingAsync();
            return (download.Value.Content, download.Value.Details.ContentType);
        }

        public async Task DeleteAsync(string blobName)
        {
            await _container.GetBlobClient(blobName).DeleteIfExistsAsync();
        }

    }
}