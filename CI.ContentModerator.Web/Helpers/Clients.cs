using Microsoft.Azure.CognitiveServices.ContentModerator;

namespace CI.ContentModerator.Web.Helpers
{
    public static class Clients
    {
        public static ContentModeratorClient GetContentModeratorClient(string azureRegion, string subscriptionKey)
        {
            var azureBaseUrl =
                $"https://{azureRegion}.api.cognitive.microsoft.com";

            var client =
                new ContentModeratorClient(new ApiKeyServiceClientCredentials(subscriptionKey))
                {
                    Endpoint = azureBaseUrl
                };

            return client;
        }
    }
}