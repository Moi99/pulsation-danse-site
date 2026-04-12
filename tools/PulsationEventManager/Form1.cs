using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulsationEventManager;

public partial class Form1 : Form
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private readonly List<EventItem> events = [];

    private string siteRoot = "";
    private string eventsPath = "";

    private Label siteRootLabel = null!;
    private ListBox eventList = null!;
    private TextBox titleInput = null!;
    private TextBox urlInput = null!;
    private TextBox dateInput = null!;
    private TextBox endDateInput = null!;
    private TextBox timeInput = null!;
    private TextBox locationInput = null!;
    private ComboBox typeInput = null!;
    private CheckedListBox danceInput = null!;
    private TextBox imageInput = null!;
    private TextBox imageAltInput = null!;
    private Label statusLabel = null!;
    private Button saveButton = null!;
    private Button deleteButton = null!;
    private Button openFacebookButton = null!;

    public Form1()
    {
        InitializeComponent();
        BuildUi();

        siteRoot = FindSiteRoot() ?? "";

        if (string.IsNullOrWhiteSpace(siteRoot))
        {
            PromptForSiteRoot();
        }
        else
        {
            LoadEvents();
        }
    }

    private void BuildUi()
    {
        Text = "Pulsation Danse - Gestion des événements Facebook";
        MinimumSize = new Size(1060, 720);
        Size = new Size(1180, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = Color.FromArgb(247, 239, 231)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 10)
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(topBar, 0, 0);

        siteRootLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Dossier du site: non détecté",
            TextAlign = ContentAlignment.MiddleLeft
        };
        topBar.Controls.Add(siteRootLabel, 0, 0);

        var chooseRootButton = new Button
        {
            AutoSize = true,
            Text = "Choisir le dossier du site"
        };
        chooseRootButton.Click += (_, _) => PromptForSiteRoot();
        topBar.Controls.Add(chooseRootButton, 1, 0);

        var reloadButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Recharger"
        };
        reloadButton.Click += (_, _) => LoadEvents();
        topBar.Controls.Add(reloadButton, 2, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 370,
            FixedPanel = FixedPanel.Panel1
        };
        root.Controls.Add(split, 0, 1);

        BuildListPanel(split.Panel1);
        BuildFormPanel(split.Panel2);

        statusLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(92, 39, 56),
            Padding = new Padding(0, 10, 0, 0),
            Text = "Prêt."
        };
        root.Controls.Add(statusLabel, 0, 2);
    }

    private void BuildListPanel(Control parent)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parent.Controls.Add(panel);

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Événements"
        }, 0, 0);

        eventList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        eventList.SelectedIndexChanged += (_, _) => LoadSelectedEventIntoForm();
        panel.Controls.Add(eventList, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 10, 0, 0),
            WrapContents = true
        };
        panel.Controls.Add(buttons, 0, 2);

        var newButton = new Button { AutoSize = true, Text = "Nouveau" };
        newButton.Click += (_, _) => ClearForm();
        buttons.Controls.Add(newButton);

        saveButton = new Button { AutoSize = true, Text = "Enregistrer" };
        saveButton.Click += (_, _) => SaveCurrentEvent();
        buttons.Controls.Add(saveButton);

        deleteButton = new Button { AutoSize = true, Text = "Supprimer" };
        deleteButton.Click += (_, _) => DeleteCurrentEvent();
        buttons.Controls.Add(deleteButton);
    }

    private void BuildFormPanel(Control parent)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 3,
            RowCount = 12,
            Padding = new Padding(12, 0, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        parent.Controls.Add(panel);

        titleInput = AddTextRow(panel, 0, "Titre", "");
        urlInput = AddTextRow(panel, 1, "URL Facebook", "");
        dateInput = AddTextRow(panel, 2, "Date début", "2026-05-16");
        endDateInput = AddTextRow(panel, 3, "Date fin", "Optionnel - 2026-05-17");
        timeInput = AddTextRow(panel, 4, "Heure", "20h");
        locationInput = AddTextRow(panel, 5, "Lieu", "Québec");

        AddLabel(panel, 6, "Type");
        typeInput = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        typeInput.Items.AddRange(["session", "soiree-locale", "weekender"]);
        typeInput.SelectedIndex = 1;
        panel.Controls.Add(typeInput, 1, 6);

        AddLabel(panel, 7, "Danse(s)");
        danceInput = new CheckedListBox
        {
            Dock = DockStyle.Top,
            Height = 92,
            CheckOnClick = true
        };
        danceInput.Items.AddRange(["WCS", "Zouk", "Blues"]);
        panel.Controls.Add(danceInput, 1, 7);

        imageInput = AddTextRow(panel, 8, "Image", "assets/images/events/nom-evenement.jpg");
        panel.SetColumnSpan(imageInput, 1);
        var browseImageButton = new Button
        {
            AutoSize = true,
            Text = "Importer..."
        };
        browseImageButton.Click += (_, _) => ImportImage();
        panel.Controls.Add(browseImageButton, 2, 8);

        imageAltInput = AddTextRow(panel, 9, "Texte image", "");

        var help = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(620, 0),
            Padding = new Padding(0, 12, 0, 12),
            Text = "Dates au format AAAA-MM-JJ. Si la date fin est vide, l'événement est considéré terminé après sa date de début. L'image peut être ajoutée plus tard; l'utilitaire copie les images importées dans assets/images/events."
        };
        panel.Controls.Add(help, 1, 10);
        panel.SetColumnSpan(help, 2);

        var actionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        panel.Controls.Add(actionRow, 1, 11);
        panel.SetColumnSpan(actionRow, 2);

        openFacebookButton = new Button { AutoSize = true, Text = "Ouvrir Facebook" };
        openFacebookButton.Click += (_, _) => OpenFacebookUrl();
        actionRow.Controls.Add(openFacebookButton);

        var openJsonButton = new Button { AutoSize = true, Text = "Ouvrir le JSON" };
        openJsonButton.Click += (_, _) => OpenFile(eventsPath);
        actionRow.Controls.Add(openJsonButton);
    }

    private TextBox AddTextRow(TableLayoutPanel panel, int row, string label, string placeholder)
    {
        AddLabel(panel, row, label);

        var input = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = placeholder
        };
        panel.Controls.Add(input, 1, row);
        panel.SetColumnSpan(input, 2);
        return input;
    }

    private static void AddLabel(TableLayoutPanel panel, int row, string text)
    {
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 4, 12, 0),
            Text = text
        }, 0, row);
    }

    private void LoadEvents()
    {
        if (string.IsNullOrWhiteSpace(siteRoot))
        {
            return;
        }

        eventsPath = Path.Combine(siteRoot, "data", "evenements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(eventsPath)!);

        if (!File.Exists(eventsPath))
        {
            File.WriteAllText(eventsPath, JsonSerializer.Serialize(new EventStore(), jsonOptions));
        }

        var store = ReadStore();
        WriteJavaScriptStore(store);
        events.Clear();
        events.AddRange(store.Events.OrderBy(item => item.Date).ThenBy(item => item.Title));
        RefreshEventList();
        UpdateSiteRootLabel();
        SetStatus($"{events.Count} événement(s) chargé(s).");
    }

    private EventStore ReadStore()
    {
        try
        {
            var json = File.ReadAllText(eventsPath);
            return JsonSerializer.Deserialize<EventStore>(json, jsonOptions) ?? new EventStore();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible de lire le fichier JSON.\n\n{ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return new EventStore();
        }
    }

    private void WriteStore()
    {
        var store = new EventStore
        {
            Events = events.OrderBy(item => item.Date).ThenBy(item => item.Title).ToList()
        };
        File.WriteAllText(eventsPath, JsonSerializer.Serialize(store, jsonOptions));
        WriteJavaScriptStore(store);
    }

    private void WriteJavaScriptStore(EventStore store)
    {
        var eventsScriptPath = Path.Combine(siteRoot, "data", "evenements.js");
        var json = JsonSerializer.Serialize(store, jsonOptions);
        var script = $"window.PULSATION_EVENTS = {json};{Environment.NewLine}";
        File.WriteAllText(eventsScriptPath, script);
    }

    private void RefreshEventList()
    {
        var selected = eventList.SelectedItem as EventItem;

        eventList.BeginUpdate();
        eventList.Items.Clear();

        foreach (var item in events)
        {
            eventList.Items.Add(item);
        }

        eventList.EndUpdate();

        if (selected is not null && events.Contains(selected))
        {
            eventList.SelectedItem = selected;
        }
    }

    private void LoadSelectedEventIntoForm()
    {
        if (eventList.SelectedItem is not EventItem item)
        {
            return;
        }

        titleInput.Text = item.Title;
        urlInput.Text = item.Url;
        dateInput.Text = item.Date;
        endDateInput.Text = item.EndDate;
        timeInput.Text = item.Time;
        locationInput.Text = item.Location;
        typeInput.SelectedItem = string.IsNullOrWhiteSpace(item.Type) ? "soiree-locale" : item.Type;
        imageInput.Text = item.Image;
        imageAltInput.Text = item.ImageAlt;

        for (var index = 0; index < danceInput.Items.Count; index++)
        {
            var value = danceInput.Items[index]?.ToString() ?? "";
            danceInput.SetItemChecked(index, item.Dance.Any(dance => string.Equals(dance, value, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void ClearForm()
    {
        eventList.ClearSelected();
        titleInput.Clear();
        urlInput.Clear();
        dateInput.Clear();
        endDateInput.Clear();
        timeInput.Clear();
        locationInput.Clear();
        typeInput.SelectedItem = "soiree-locale";
        imageInput.Clear();
        imageAltInput.Clear();

        for (var index = 0; index < danceInput.Items.Count; index++)
        {
            danceInput.SetItemChecked(index, false);
        }

        titleInput.Focus();
        SetStatus("Nouvel événement prêt à remplir.");
    }

    private void SaveCurrentEvent()
    {
        if (!ValidateForm(out var message))
        {
            MessageBox.Show(message, "Information manquante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var item = eventList.SelectedItem as EventItem ?? new EventItem();

        item.Title = titleInput.Text.Trim();
        item.Url = urlInput.Text.Trim();
        item.Date = dateInput.Text.Trim();
        item.EndDate = endDateInput.Text.Trim();
        item.Time = timeInput.Text.Trim();
        item.Location = locationInput.Text.Trim();
        item.Type = typeInput.SelectedItem?.ToString() ?? "soiree-locale";
        item.Image = NormalizeAssetPath(imageInput.Text.Trim());
        item.ImageAlt = imageAltInput.Text.Trim();
        item.Dance = danceInput.CheckedItems.Cast<object>().Select(value => value.ToString() ?? "").Where(value => value.Length > 0).ToList();

        if (!events.Contains(item))
        {
            events.Add(item);
        }

        WriteStore();
        RefreshEventList();
        eventList.SelectedItem = item;
        SetStatus("Événement enregistré.");
    }

    private void DeleteCurrentEvent()
    {
        if (eventList.SelectedItem is not EventItem item)
        {
            return;
        }

        var result = MessageBox.Show($"Supprimer l'événement suivant?\n\n{item.Title}", "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        events.Remove(item);
        WriteStore();
        RefreshEventList();
        ClearForm();
        SetStatus("Événement supprimé.");
    }

    private bool ValidateForm(out string message)
    {
        if (string.IsNullOrWhiteSpace(titleInput.Text))
        {
            message = "Le titre est obligatoire.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(urlInput.Text) || !Uri.TryCreate(urlInput.Text.Trim(), UriKind.Absolute, out _))
        {
            message = "L'URL Facebook doit être une URL complète.";
            return false;
        }

        if (!IsValidDate(dateInput.Text.Trim()))
        {
            message = "La date de début doit être au format AAAA-MM-JJ.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(endDateInput.Text) && !IsValidDate(endDateInput.Text.Trim()))
        {
            message = "La date de fin doit être vide ou au format AAAA-MM-JJ.";
            return false;
        }

        if (danceInput.CheckedItems.Count == 0)
        {
            message = "Sélectionne au moins un style de danse.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool IsValidDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);

    private void ImportImage()
    {
        if (string.IsNullOrWhiteSpace(siteRoot))
        {
            MessageBox.Show("Choisis d'abord le dossier du site.", "Dossier manquant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.gif|Tous les fichiers|*.*",
            Title = "Choisir une image d'événement"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var eventsImageFolder = Path.Combine(siteRoot, "assets", "images", "events");
        Directory.CreateDirectory(eventsImageFolder);

        var extension = Path.GetExtension(dialog.FileName);
        var baseName = Slugify(string.IsNullOrWhiteSpace(titleInput.Text) ? Path.GetFileNameWithoutExtension(dialog.FileName) : titleInput.Text);
        var fileName = $"{baseName}{extension}";
        var destination = Path.Combine(eventsImageFolder, fileName);
        var counter = 2;

        while (File.Exists(destination))
        {
            fileName = $"{baseName}-{counter}{extension}";
            destination = Path.Combine(eventsImageFolder, fileName);
            counter++;
        }

        File.Copy(dialog.FileName, destination);
        imageInput.Text = $"assets/images/events/{fileName}";
        SetStatus($"Image importée: {imageInput.Text}");
    }

    private void OpenFacebookUrl()
    {
        if (Uri.TryCreate(urlInput.Text.Trim(), UriKind.Absolute, out var uri))
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
    }

    private static void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private void PromptForSiteRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choisir le dossier racine du site Pulsation Danse",
            UseDescriptionForTitle = true
        };

        if (Directory.Exists(siteRoot))
        {
            dialog.InitialDirectory = siteRoot;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            UpdateSiteRootLabel();
            return;
        }

        siteRoot = dialog.SelectedPath;
        LoadEvents();
    }

    private void UpdateSiteRootLabel()
    {
        siteRootLabel.Text = string.IsNullOrWhiteSpace(siteRoot)
            ? "Dossier du site: non détecté"
            : $"Dossier du site: {siteRoot}";
    }

    private void SetStatus(string message)
    {
        statusLabel.Text = message;
    }

    private static string? FindSiteRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ou-danser.html")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "data")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string NormalizeAssetPath(string value) =>
        value.Replace('\\', '/');

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();

        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "evenement" : slug;
    }
}

public sealed class EventStore
{
    [JsonPropertyName("events")]
    public List<EventItem> Events { get; set; } = [];
}

public sealed class EventItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("endDate")]
    public string EndDate { get; set; } = "";

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("dance")]
    public List<string> Dance { get; set; } = [];

    [JsonPropertyName("type")]
    public string Type { get; set; } = "soiree-locale";

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("imageAlt")]
    public string ImageAlt { get; set; } = "";

    public override string ToString()
    {
        var dance = Dance.Count > 0 ? string.Join("/", Dance) : "Danse?";
        var type = string.IsNullOrWhiteSpace(Type) ? "type?" : Type;
        var title = string.IsNullOrWhiteSpace(Title) ? "Sans titre" : Title;
        var date = string.IsNullOrWhiteSpace(Date) ? "date?" : Date;
        return $"{date} · {dance} · {type} · {title}";
    }
}
