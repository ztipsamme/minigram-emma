using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace backend.Services
{
    public class ImageService
    {
        private readonly ImageStorageService _imageStorageService;

        public ImageService(ImageStorageService imageStorageService)
        {
            _imageStorageService = imageStorageService;
        }

        public async Task<List<Image>> GetAllAsync()
        {
            var images = new List<Image>();

            await foreach (var blob in _imageStorageService.GetBlobsAsync())
            {
                var image = _imageStorageService.CreateImageFromBlob(blob);
                if (image is not null)
                {
                    images.Add(image);
                }
            }

            return images;
        }

        public async Task<Image?> GetByIdAsync(int id)
        {
            var blobClient = await _imageStorageService.FindImageBlob(id);
            if (blobClient is null)
                return null;

            var properties = await blobClient.GetPropertiesAsync();
            var metadata = properties.Value.Metadata;

            var originalName = metadata.TryGetValue("originalName", out var name)
                ? name
                : blobClient.Name;

            var caption = metadata.TryGetValue("caption", out var captionValue)
                ? captionValue
                : "";

            var tags = metadata.TryGetValue("tags", out var tagsValue)
                ? JsonSerializer.Deserialize<List<string>>(tagsValue) ?? []
                : [];

            return new Image(id, originalName, caption, tags, $"/bilder/{id}/image");
        }

        public async Task<BlobClient?> FindImageBlobAsync(int id) =>
            await _imageStorageService.FindImageBlob(id);

        public async Task<int> GetNextIdAsync() =>
            await _imageStorageService.GetNextId();

        public async Task<BlobClient> CreateAsync(NewImage newImage)
        {
            using var httpClient = new HttpClient();
            var imageBytes = await httpClient.GetByteArrayAsync(newImage.Url);

            var id = await _imageStorageService.GetNextId();
            var extension = Path.GetExtension(newImage.Name);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            var blobName = $"{id}{extension}";
            var blobClient = _imageStorageService.GetBlobClient(blobName);

            using var imageStream = new MemoryStream(imageBytes);

            await blobClient.UploadAsync(
                imageStream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = ImageStorageService.GetContentType(newImage.Name)
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["originalName"] = newImage.Name,
                        ["caption"] = newImage.Caption,
                        ["tags"] = JsonSerializer.Serialize(newImage.Tags ?? [])
                    }
                });

            return blobClient;
        }
    }
}