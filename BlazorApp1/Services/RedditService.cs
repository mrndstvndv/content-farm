using HtmlAgilityPack;

namespace BlazorApp1.Services;

public class RedditService(IHttpClientFactory clientFactory)
{
    private static readonly string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public async Task<(string? Error, string? Title, string? Text)> GetPost(string url)
    {
        var client = clientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        var resolved = await ResolveUrl(url, client);
        var html = await client.GetStringAsync(resolved);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var titleNode = doc.DocumentNode.SelectSingleNode(
            "//a[contains(@class, 'title') and contains(@class, 'may-blank')]");
        var title = titleNode?.InnerText.Trim();

        var postThing = doc.DocumentNode.SelectSingleNode(
            "//div[contains(@class, 'thing') and contains(@data-type, 'link')]");
        var bodyNode = postThing?.SelectSingleNode(
            ".//div[contains(@class, 'usertext-body')]//div[contains(@class, 'md')]");
        var body = bodyNode?.InnerText.Trim();

        var errors = new List<string>();
        if (title is null) errors.Add("title not found");
        if (body is null) errors.Add("body not found");

        return (errors.Count > 0 ? string.Join("; ", errors) : null, title, body);
    }

    private static async Task<string> ResolveUrl(string url, HttpClient client)
    {
        var uri = new Uri(url);

        if (uri.Host == "old.reddit.com" && uri.AbsolutePath.Contains("/comments/"))
            return url;

        // old.reddit.com doesn't support /s/ shortlinks, resolve via www.reddit.com first
        if (uri.AbsolutePath.Contains("/s/"))
        {
            var resolveUri = new UriBuilder(uri) { Host = "www.reddit.com" }.Uri;

            using var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, resolveUri),
                HttpCompletionOption.ResponseHeadersRead);

            var finalUri = response.RequestMessage?.RequestUri ?? resolveUri;

            var path = finalUri.AbsolutePath;
            if (path.Contains("/comments/"))
                return $"https://old.reddit.com{path}";
        }

        return new UriBuilder(uri) { Host = "old.reddit.com" }.Uri.ToString();
    }
}
