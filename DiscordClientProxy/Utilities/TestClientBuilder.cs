using System.Text.RegularExpressions;
using AngleSharp.Html;
using AngleSharp.Html.Parser;

namespace DiscordClientProxy.Utilities;

public class TestClientBuilder
{
    public static async Task PrefetchClient()
    {
        if (!Configuration.Instance.Cache.PreloadFromWeb) return;
        Console.WriteLine("[TestClientBuilder] Fetching missing assets...");

        var originalHtml = await GetOriginalHtml();
        var lines = originalHtml.Split("\n");

        var preloadScripts = await GetPreloadScripts(lines);
        var mainScripts = await GetMainScripts(lines);
        var css = await GetCss(lines);

        Console.WriteLine($"[TestClientBuilder] Found {preloadScripts.Length + mainScripts.Length + css.Length} assets");
        preloadScripts = preloadScripts.Where(x => !File.Exists($"{Configuration.Instance.AssetCacheLocationResolved}/{x.Replace("/assets/", "")}")).ToArray();
        mainScripts = mainScripts.Where(x => !File.Exists($"{Configuration.Instance.AssetCacheLocationResolved}/{x.Replace("/assets/", "")}")).ToArray();
        css = css.Where(x => !File.Exists($"{Configuration.Instance.AssetCacheLocationResolved}/{x.Replace("/assets/", "")}")).ToArray();

        Console.WriteLine($"[TestClientBuilder] Found {preloadScripts.Length + mainScripts.Length + css.Length} assets");
        await Task.WhenAll(mainScripts.Select(async x => { await AssetCache.GetFromNetwork(x.Replace("/assets/", "")); }));
        await Task.WhenAll(css.Select(async x => { await AssetCache.GetFromNetwork(x.Replace("/assets/", "")); }));
        var throttler = new SemaphoreSlim(System.Environment.ProcessorCount * 4);
        var tasks = preloadScripts.Select(async x =>
        {
            await throttler.WaitAsync();
            await AssetCache.GetFromNetwork(x.Replace("/assets/", ""));
            throttler.Release();
        }).ToList();
        await Task.WhenAll(tasks);
    }

    private static async Task<string> GetHtmlFormatted(string url)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        //format html
        var parser = new HtmlParser();

        var document = parser.ParseDocument(html);

        var sw = new StringWriter();
        document.ToHtml(sw, new PrettyMarkupFormatter());
        return sw.ToString();
    }
    private static async Task<string> GetOriginalHtml(string suffix = "")
    {
        var targetVer = Configuration.Instance.Version;
        if (targetVer.Contains("latest"))
        {
            var response = await GetHtmlFormatted(
                (targetVer == "latest "
                    ? "https://discordapp.com/app"
                    : targetVer == "latest-ptb"
                        ? "https://ptb.discordapp.com/app"
                        : "https://canary.discordapp.com/app"
                ) + suffix
            );
            return response;
        }

        if (Directory.Exists(Configuration.Instance.AssetCacheLocationResolved) && File.Exists(Path.Combine(Configuration.Instance.AssetCacheLocationResolved, "index.html")))
            return File.ReadAllText(Path.Combine(Configuration.Instance.AssetCacheLocationResolved, "index.html"));

        throw new Exception("Could not find index.html");
    }

    public static async Task BuildClientPage()
    {
        var html = await File.ReadAllTextAsync(Environment.BinDir + "/Resources/Pages/index.html");
        var originalHtml = await GetOriginalHtml();
        var lines = originalHtml.Split("\n");

        var preloadScripts = await GetPreloadScripts(lines);
        var mainScripts = await GetMainScripts(lines);
        var css = await GetCss(lines);

        if (Configuration.Instance.Client.AddPrefetchTags)
            html = html.Replace("<!--prefetch_script-->",
                string.Join("\n", preloadScripts.Select(x => $"<link rel=\"prefetch\" as=\"script\" href=\"{x}\" />")));
        html = html.Replace("<!--client_script-->",
            string.Join("\n", mainScripts.Select(x => $"<script src=\"{x}\"></script>")));
        html = html.Replace("<!--client_css-->",
            string.Join("\n", css.Select(x => $"<link rel=\"stylesheet\" href=\"{x}\" />")));


        //inject debug utilities
        var debugOptions = Configuration.Instance.Client.DebugOptions;
        if (debugOptions.DumpWebsocketTrafficToBrowserConsole)
            html = html.Replace("<!-- preload plugin marker -->",
                await File.ReadAllTextAsync(Environment.BinDir + "/Resources/Private/Injections/WebSocketDataLog.html") +
                "\n<!-- preload plugin marker -->");
        if (debugOptions.DumpWebsocketTraffic)
            html = html.Replace("<!-- preload plugin marker -->",
                await File.ReadAllTextAsync(Environment.BinDir + "/Resources/Private/Injections/WebSocketDumper.html") +
                "\n<!-- preload plugin marker -->");

        AssetCache.Instance.ClientPageHtml = html;
    }

    public static async Task BuildDevPage()
    {
        var html = await File.ReadAllTextAsync(Environment.BinDir + "/Resources/Pages/developers.html");

        var originalHtml = await GetOriginalHtml("/developers");
        var lines = originalHtml.Split("\n");

        var preloadScripts = await GetPreloadScripts(lines);
        var mainScripts = await GetMainScripts(lines);
        var css = await GetCss(lines);

        if (Configuration.Instance.Client.AddPrefetchTags)
            html = html.Replace("<!--prefetch_script-->",
                string.Join("\n", preloadScripts.Select(x => $"<link rel=\"prefetch\" as=\"script\" href=\"{x}\" />")));
        html = html.Replace("<!--client_script-->",
            string.Join("\n", mainScripts.Select(x => $"<script src=\"{x}\"></script>")));
        html = html.Replace("<!--client_css-->",
            string.Join("\n", css.Select(x => $"<link rel=\"stylesheet\" href=\"{x}\" />")));


        //inject debug utilities
        var debugOptions = Configuration.Instance.Client.DebugOptions;
        if (debugOptions.DumpWebsocketTrafficToBrowserConsole)
            html = html.Replace("<!-- preload plugin marker -->",
                await File.ReadAllTextAsync(Environment.BinDir + "/Resources/Private/Injections/WebSocketDataLog.html") +
                "\n<!-- preload plugin marker -->");
        if (debugOptions.DumpWebsocketTraffic)
            html = html.Replace("<!-- preload plugin marker -->",
                await File.ReadAllTextAsync(Environment.BinDir + "/Resources/Private/Injections/WebSocketDumper.html") +
                "\n<!-- preload plugin marker -->");

        AssetCache.Instance.DevPageHtml = html;
    }

    public static async Task<string[]> GetPreloadScripts(string[]? lines = null)
    {
        lines ??= (await GetOriginalHtml()).Split("\n");
        List<string> scripts = new();
        var prefetchScriptHtml = lines.Where(x => x.Contains("link rel=\"prefetch\" as=\"script\"")).ToList();
        foreach (var script in prefetchScriptHtml)
        {
            var match = Regex.Match(script, "href=\"(.*?)\"");
            if (match.Success) scripts.Add(match.Groups[1].Value);
        }

        return scripts.ToArray();
    }

    public static async Task<string[]> GetMainScripts(string[]? lines = null)
    {
        lines ??= (await GetOriginalHtml()).Split("\n");
        List<string> scripts = new();
        var mainScriptHtml = lines.Where(x => x.Contains("<script src=")).ToList();
        foreach (var script in mainScriptHtml)
        {
            var match = Regex.Match(script, "src=\"(.*?)\"");
            if (match.Success) scripts.Add(match.Groups[1].Value);
        }

        return scripts.ToArray();
    }

    public static async Task<string[]> GetCss(string[]? lines = null)
    {
        lines ??= (await GetOriginalHtml()).Split("\n");
        List<string> stylesheets = new();
        var cssHtml = lines.Where(x => x.Contains("<link rel=\"stylesheet\"")).ToList();
        foreach (var script in cssHtml)
        {
            var match = Regex.Match(script, "href=\"(.*?)\"");
            if (match.Success) stylesheets.Add(match.Groups[1].Value);
        }

        return stylesheets.ToArray();
    }
}