using System.IO;
using System.Text;
using Microsoft.Web.WebView2.Core;

internal static partial class Program
{
    private static async Task VerifyThumbnailRecoveryAsync(CoreWebView2 core, CoreWebView2Environment environment)
    {
        var requests = new Dictionary<string, int>();
        CoreWebView2Deferral? stalled = null;
        const string fixture = """
            <!doctype html><title>Thumbnail recovery fixture</title>
            <style>yt-image { display:block; height:180px } img { width:240px; height:135px }</style>
            <body><script>
            for (let i=0; i<150; i++) {
                const card = document.createElement('yt-image');
                card.innerHTML = `<img loading="lazy" src="https://i.ytimg.com/vi/ok${i}/hqdefault.jpg">`;
                document.body.append(card);
            }
            window.addCard = (id) => {
                const card = document.createElement('yt-image');
                card.innerHTML = `<img id="${id}" src="https://i.ytimg.com/vi/${id}/hqdefault.jpg">`;
                document.body.append(card);
                card.scrollIntoView();
            };
            </script>
            """;
        core.AddWebResourceRequestedFilter("https://www.youtube.com/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://i.ytimg.com/*", CoreWebView2WebResourceContext.All);
        void Respond(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var uri = new Uri(args.Request.Uri);
            if (uri.Host == "www.youtube.com")
            {
                args.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(fixture)),
                    200, "OK", "Content-Type: text/html; charset=utf-8");
                return;
            }
            if (uri.Host != "i.ytimg.com") return;
            var id = uri.AbsolutePath.Split('/')[2];
            requests[id] = requests.GetValueOrDefault(id) + 1;
            if (id == "stalled" && requests[id] == 1)
            {
                stalled = args.GetDeferral();
                return;
            }
            var fail = id == "permanent" || (!id.StartsWith("ok") && requests[id] == 1);
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='135'><rect width='240' height='135' fill='green'/></svg>";
            args.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(fail ? "Unavailable" : svg)),
                fail ? 503 : 200, fail ? "Unavailable" : "OK", "Content-Type: image/svg+xml\r\nCache-Control: no-store");
        }
        core.WebResourceRequested += Respond;
        try
        {
            core.Navigate("https://www.youtube.com/feed/recovery-test");
            await WaitForAsync(() => core.DocumentTitle == "Thumbnail recovery fixture", "thumbnail fixture");
            // Exercise a long feed, then repeated dynamic additions as in infinite scrolling.
            for (var i = 0; i < 150; i += 10)
            {
                await core.ExecuteScriptAsync($"document.querySelectorAll('yt-image')[{i}].scrollIntoView()");
                await Task.Delay(60);
            }
            for (var i = 0; i < 4; i++)
            {
                await core.ExecuteScriptAsync($"addCard('retry{i}')");
                await WaitForImageAsync(core, $"retry{i}");
                Check(requests[$"retry{i}"] == 2, "Failed thumbnail must recover with one retry");
            }
            Check(await core.ExecuteScriptAsync("scrollY > 20000") == "true", "Recovery must preserve deep scroll position");
            await core.ExecuteScriptAsync("document.querySelector('#retry3').src = 'https://i.ytimg.com/vi/recycled/hqdefault.jpg'");
            await WaitForImageAsync(core, "retry3");
            Check(requests.GetValueOrDefault("recycled") == 2, "Recycled card must recover its new image URL");

            await core.ExecuteScriptAsync("addCard('permanent')");
            await WaitForAsync(() => requests.GetValueOrDefault("permanent") == 3, "bounded image retries");
            await Task.Delay(6000);
            Check(requests["permanent"] == 3, "Permanent failure must stop after two retries");
            await core.ExecuteScriptAsync("addCard('offscreen'); scrollTo(0,0)");
            await Task.Delay(4500);
            Check(requests.GetValueOrDefault("offscreen") == 1, "Offscreen images must not be retried");
            await core.ExecuteScriptAsync("document.querySelector('#offscreen').scrollIntoView()");
            await WaitForImageAsync(core, "offscreen");

            await core.ExecuteScriptAsync("addCard('stalled')");
            await WaitForImageAsync(core, "stalled", 28);
            Check(requests.GetValueOrDefault("stalled") == 2, "Stalled visible image must recover without page reload");
            Check(requests.Where(pair => pair.Key.StartsWith("ok")).All(pair => pair.Value == 1),
                "Successfully loaded thumbnails must not be requested again");
            Console.WriteLine("PASS thumbnail recovery: deep scrolling, appended/recycled cards, failed/stalled images, retry cap, offscreen isolation");
        }
        finally
        {
            stalled?.Complete();
            core.WebResourceRequested -= Respond;
        }
    }

    private static async Task WaitForImageAsync(CoreWebView2 core, string id, int seconds = 12)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (await core.ExecuteScriptAsync($"(() => {{ const i=document.getElementById('{id}'); return i.complete && i.naturalWidth > 0; }})()") != "true")
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"Image {id} did not recover");
            await Task.Delay(100);
        }
    }
}
