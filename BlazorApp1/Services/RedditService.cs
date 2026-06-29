using HtmlAgilityPack;
using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class RedditService(IHttpClientFactory clientFactory, ILogger<RedditService> logger)
{
    private static readonly string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public async Task<PostData> GetPost(string url)
    {
        var client = clientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.Timeout = TimeSpan.FromSeconds(10);

        var resolved = await ResolveUrl(url, client);

        string html;
        try
        {
            html = await client.GetStringAsync(resolved);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RedditFetchException(url, ex.Message, ex);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode(
            "//a[contains(@class, 'title') and contains(@class, 'may-blank')]")?.InnerText.Trim();

        if (title is null)
            throw new RedditPostNotFoundException(url, "title not found");

        var postNode = doc.DocumentNode.SelectSingleNode(
            "//div[contains(@class, 'thing') and contains(@data-type, 'link')]");

        var body = postNode?.SelectSingleNode(".//div[contains(@class, 'usertext-body')]//div[contains(@class, 'md')]")
            ?.InnerText.Trim();

        if (body is null)
            throw new RedditPostNotFoundException(url, "body not found");

        var author = postNode?.GetAttributeValue("data-author", "username") ?? "username";

        return new PostData(title, body, author, resolved);
    }

    private static async Task<string> ResolveUrl(string url, HttpClient client)
    {
        Uri uri;
        try
        {
            uri = new Uri(url);
        }
        catch (UriFormatException ex)
        {
            throw new RedditFetchException(url, $"invalid URL: {ex.Message}", ex);
        }

        if (uri.Host == "old.reddit.com" && uri.AbsolutePath.Contains("/comments/"))
            return url;

        if (uri.AbsolutePath.Contains("/s/"))
        {
            var resolveUri = new UriBuilder(uri) { Host = "www.reddit.com" }.Uri;

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, resolveUri),
                    HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new RedditFetchException(url, $"resolve failed: {ex.Message}", ex);
            }

            using (response)
            {
                var finalUri = response.RequestMessage?.RequestUri ?? resolveUri;
                var path = finalUri.AbsolutePath;
                if (path.Contains("/comments/"))
                    return $"https://old.reddit.com{path}";
            }
        }

        return new UriBuilder(uri) { Host = "old.reddit.com" }.Uri.ToString();
    }
}


