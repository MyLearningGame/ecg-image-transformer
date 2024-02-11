// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImageFunctions
{
    public static class Thumbnail
    {
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        private static string GetBlobNameFromUrl(string bloblUrl, string keyword)
        {
            var uri = new Uri(bloblUrl);
            var updatedUri = AppendKeywordBeforeExtension(uri, keyword);
            var blobClient = new BlobClient(updatedUri);
            return blobClient.Name;
        }

        private static Uri AppendKeywordBeforeExtension(Uri url, string keyword)
        {
            // Get the original URL as a string
            string originalUrl = url.ToString();

            // Find the last occurrence of '.' in the URL
            int lastDotIndex = originalUrl.LastIndexOf('.');

            // If a '.' is found and it's before the query string or fragment,
            // append the keyword before the extension
            if (lastDotIndex > 0 && originalUrl[lastDotIndex - 1] != '/' && originalUrl[lastDotIndex - 1] != '?' && originalUrl[lastDotIndex - 1] != '#')
            {
                string newUrl = originalUrl.Insert(lastDotIndex, keyword);
                return new Uri(newUrl);
            }

            // If no '.' is found or it's after the query string or fragment, 
            // append the keyword at the end of the URL
            return new Uri(originalUrl + keyword);
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        [FunctionName("Thumbnail")]
        public static async Task Run(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {
            try
            {
                if (input != null)
                {
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        var bigWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("BIG_WIDTH"));
                        var mediumWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("MEDIUM_WIDTH"));
                        var smallWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("SMALL_WIDTH"));
                        Dictionary<int, string> myDictionary = new Dictionary<int, string>
                        {
                            { bigWidth, "-b" },
                            { mediumWidth, "-m" },
                            { smallWidth, "-s" }
                        };

                        var thumbContainerName = Environment.GetEnvironmentVariable("OUTPUT_CONTAINER_NAME");
                        var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                        var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                        
                        foreach (KeyValuePair<int, string> pair in myDictionary)
                        {
                            int width = pair.Key;
                            string postfix = pair.Value;
                            var blobName = GetBlobNameFromUrl(createdEvent.Url, postfix);

                            using (var output = new MemoryStream())
                            using (Image<Rgba32> image = Image.Load(input))
                            {
                                var divisor = image.Width / width;
                                var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                                image.Mutate(x => x.Resize(width, height));
                                image.Save(output, encoder);
                                output.Position = 0;
                                await blobContainerClient.UploadBlobAsync(blobName, output);
                            }
                        }
                    }
                    else
                    {
                        log.LogInformation($"No encoder support for: {createdEvent.Url}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }
    }
}
