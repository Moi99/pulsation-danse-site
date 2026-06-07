using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PulsationEventManager;

public sealed class FacebookBrowserImportForm : Form
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string eventUrl;
    private readonly FacebookEventImporter importer;
    private readonly string siteRoot;

    private readonly WebView2 webView = new();
    private readonly Label statusLabel = new();
    private readonly Button usePageButton = new();

    public FacebookBrowserImportForm(string eventUrl, FacebookEventImporter importer, string siteRoot)
    {
        this.eventUrl = eventUrl;
        this.importer = importer;
        this.siteRoot = siteRoot;

        Text = "Import Facebook - navigateur intégré";
        MinimumSize = new Size(1040, 760);
        Size = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10F);

        BuildUi();
        Load += async (_, _) => await InitializeBrowserAsync();
    }

    public FacebookImportResult? Result { get; private set; }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var help = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(1100, 0),
            Padding = new Padding(0, 0, 0, 10),
            Text = "Facebook bloque parfois l'import direct. Connecte-toi si nécessaire, attends que la page de l'événement soit affichée, puis clique Utiliser cette page."
        };
        root.Controls.Add(help, 0, 0);

        webView.Dock = DockStyle.Fill;
        root.Controls.Add(webView, 0, 1);

        var footer = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 10, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(footer, 0, 2);

        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Text = "Chargement du navigateur Facebook...";
        footer.Controls.Add(statusLabel, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        footer.Controls.Add(buttons, 1, 0);

        usePageButton.AutoSize = true;
        usePageButton.Enabled = false;
        usePageButton.Text = "Utiliser cette page";
        usePageButton.Click += async (_, _) => await UseCurrentPageAsync();
        buttons.Controls.Add(usePageButton);

        var cancelButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = "Annuler"
        };
        buttons.Controls.Add(cancelButton);

        CancelButton = cancelButton;
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PulsationDanse",
                "EventManagerWebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);

            webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                usePageButton.Enabled = true;
                statusLabel.Text = args.IsSuccess
                    ? "Page chargée. Clique Utiliser cette page quand l'événement est visible."
                    : $"Chargement incomplet: {args.WebErrorStatus}. Tu peux te connecter ou réessayer.";
            };

            webView.CoreWebView2.Navigate(eventUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'ouvrir le navigateur intégré.\n\n{ex.Message}", "Navigateur Facebook", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private async Task UseCurrentPageAsync()
    {
        if (webView.CoreWebView2 is null)
        {
            return;
        }

        usePageButton.Enabled = false;
        statusLabel.Text = "Lecture de la page Facebook...";

        try
        {
            var script = """
                (() => {
                    const limit = (value, max) => String(value || "").slice(0, max);
                    const escapeHtml = (value) => limit(value, 60000)
                        .replaceAll("&", "&amp;")
                        .replaceAll("\"", "&quot;")
                        .replaceAll("<", "&lt;")
                        .replaceAll(">", "&gt;");
                    const headings = Array.from(document.querySelectorAll("h1,h2,[role='heading']"))
                        .map(element => element.innerText || element.textContent || "")
                        .filter(Boolean)
                        .slice(0, 20);
                    const activeTexts = Array.from(document.querySelectorAll("[aria-selected='true'],[aria-current],[aria-pressed='true']"))
                        .map(element => element.innerText || element.textContent || element.getAttribute("aria-label") || "")
                        .filter(Boolean)
                        .slice(0, 30);
                    const metas = Array.from(document.querySelectorAll("meta[property],meta[name]"))
                        .map(meta => ({
                            key: meta.getAttribute("property") || meta.getAttribute("name") || "",
                            content: meta.getAttribute("content") || ""
                        }))
                        .filter(meta => /title|description|image|event|start|end|place|location/i.test(meta.key + " " + meta.content))
                        .slice(0, 80);
                    const scripts = Array.from(document.scripts)
                        .map(script => script.textContent || "")
                        .filter(text => /@type|Event|startDate|start_time|endDate|end_time|cover_photo|location|place/i.test(text))
                        .slice(0, 8)
                        .map(text => limit(text, 70000));
                    const imageMap = new Map();
                    const addImage = (url, score) => {
                        if (!url || !/^https?:\/\//i.test(url) || /^blob:|^data:/i.test(url)) return;
                        if (/emoji|static_map|profile|avatar/i.test(url)) return;
                        const cleanUrl = String(url).replaceAll("&amp;", "&");
                        const previous = imageMap.get(cleanUrl) || 0;
                        if (score > previous) imageMap.set(cleanUrl, score);
                    };
                    Array.from(document.images).forEach(image => {
                        const rect = image.getBoundingClientRect();
                        const score = Math.max(
                            (image.naturalWidth || 0) * (image.naturalHeight || 0),
                            Math.round((rect.width || 0) * (rect.height || 0))
                        );
                        if (score >= 50000) {
                            addImage(image.currentSrc || image.src, score);
                        }
                    });
                    Array.from(document.querySelectorAll("*")).forEach(element => {
                        const style = getComputedStyle(element);
                        const background = style.backgroundImage || "";
                        if (!background.includes("url(")) return;
                        const rect = element.getBoundingClientRect();
                        const score = Math.round((rect.width || 0) * (rect.height || 0));
                        if (score < 50000) return;
                        for (const match of background.matchAll(/url\(["']?(.*?)["']?\)/g)) {
                            try {
                                addImage(new URL(match[1], window.location.href).href, score);
                            } catch {
                                addImage(match[1], score);
                            }
                        }
                    });
                    const imageCandidates = Array.from(imageMap.entries())
                        .sort((a, b) => b[1] - a[1])
                        .map(entry => entry[0])
                        .slice(0, 12);
                    const visibleText = [
                        activeTexts.join("\n"),
                        document.body ? document.body.innerText : ""
                    ].filter(Boolean).join("\n");
                    const compactHtml = [
                        "<html><head>",
                        metas.map(meta => `<meta name="${escapeHtml(meta.key)}" content="${escapeHtml(meta.content)}">`).join(""),
                        "</head><body>",
                        headings.map(text => `<h1>${escapeHtml(text)}</h1>`).join(""),
                        `<pre>${escapeHtml(visibleText)}</pre>`,
                        scripts.map(text => `<script type="application/json">${escapeHtml(text)}</script>`).join(""),
                        "</body></html>"
                    ].join("");
                    return JSON.stringify({
                        url: window.location.href,
                        title: document.title || "",
                        headings,
                        activeTexts,
                        imageCandidates,
                        text: limit(visibleText, 90000),
                        html: compactHtml
                    });
                })()
                """;
            var encodedResult = await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(8));
            var snapshotJson = JsonSerializer.Deserialize<string>(encodedResult) ?? "";
            var snapshot = JsonSerializer.Deserialize<BrowserSnapshot>(snapshotJson, SnapshotJsonOptions) ?? new BrowserSnapshot();

            if (string.IsNullOrWhiteSpace(snapshot.Html))
            {
                snapshot.Html = await ReadStringScriptAsync("document.documentElement ? document.documentElement.outerHTML.slice(0, 120000) : ''");
                snapshot.Url = string.IsNullOrWhiteSpace(snapshot.Url)
                    ? await ReadStringScriptAsync("window.location.href")
                    : snapshot.Url;
                snapshot.Title = string.IsNullOrWhiteSpace(snapshot.Title)
                    ? await ReadStringScriptAsync("document.title || ''")
                    : snapshot.Title;
                snapshot.Text = string.IsNullOrWhiteSpace(snapshot.Text)
                    ? await ReadStringScriptAsync("document.body ? document.body.innerText : ''")
                    : snapshot.Text;
            }

            if (string.IsNullOrWhiteSpace(snapshot.Html))
            {
                MessageBox.Show("La page affichée ne contient pas de HTML lisible.", "Import Facebook", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                usePageButton.Enabled = true;
                return;
            }

            statusLabel.Text = "Analyse des informations récupérées...";
            using var importTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Result = await importer.ImportRenderedPageAsync(
                string.IsNullOrWhiteSpace(snapshot.Url) ? eventUrl : snapshot.Url,
                snapshot.Html,
                snapshot.Title,
                snapshot.Headings,
                snapshot.Text,
                snapshot.ImageCandidates,
                siteRoot,
                importTimeout.Token);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible de lire la page affichée.\n\n{ex.Message}", "Import Facebook", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            statusLabel.Text = "Lecture impossible. Tu peux réessayer ou annuler.";
            usePageButton.Enabled = true;
        }
    }

    private async Task<string> ReadStringScriptAsync(string script)
    {
        if (webView.CoreWebView2 is null)
        {
            return "";
        }

        var encodedResult = await ExecuteScriptWithTimeoutAsync(script, TimeSpan.FromSeconds(6));
        return JsonSerializer.Deserialize<string>(encodedResult) ?? "";
    }

    private async Task<string> ExecuteScriptWithTimeoutAsync(string script, TimeSpan timeout)
    {
        if (webView.CoreWebView2 is null)
        {
            return "\"\"";
        }

        var scriptTask = webView.CoreWebView2.ExecuteScriptAsync(script);
        var completed = await Task.WhenAny(scriptTask, Task.Delay(timeout));
        if (completed != scriptTask)
        {
            throw new TimeoutException("La lecture de la page Facebook a dépassé le délai prévu.");
        }

        return await scriptTask;
    }

    private sealed class BrowserSnapshot
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Headings { get; set; } = [];
        public List<string> ActiveTexts { get; set; } = [];
        public List<string> ImageCandidates { get; set; } = [];
        public string Text { get; set; } = "";
        public string Html { get; set; } = "";
    }
}
