using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RaamTekenaar.Core;
using IOPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;
using ShapeLine = System.Windows.Shapes.Line;
using WinPdf = global::Windows.Data.Pdf;
using WinStorage = global::Windows.Storage;
using WinStreams = global::Windows.Storage.Streams;

namespace RaamTekenaar;

public partial class MainWindow : Window
{
    /// <summary>Voor de lijstweergave van plaatsingen (raamnaam opzoeken).</summary>
    public static Project? HuidigProject { get; private set; }

    private enum Modus { Selecteren, Plaatsen, Kalibreren }

    private static readonly Brush KleurNormaal = Bevroren(Color.FromRgb(0x1F, 0x4E, 0x9E));
    private static readonly Brush KleurGeselecteerd = Bevroren(Color.FromRgb(0xE0, 0x5A, 0x00));
    private static readonly Brush KleurSpook = Bevroren(Color.FromRgb(0x00, 0x8A, 0x3E));
    private static readonly Brush KleurMarker = Bevroren(Color.FromRgb(0xD0, 0x00, 0x00));

    private readonly bool _klaar;
    private Project _project = new();
    private string? _projectPad;
    private bool _gewijzigd;

    // PDF
    private string? _pdfPad;
    private List<PaginaInfo> _paginas = new();
    private WinPdf.PdfDocument? _winDoc;
    private int _pagina;
    private double _zoom = 1;
    private int _renderVersie;
    private readonly DispatcherTimer _renderTimer;

    // Interactie
    private Modus _modus = Modus.Selecteren;
    private double _mmPerPt = Schaal.NaarMmPerPt(50);
    private Point? _muis;
    private Point? _kalibratieStart;
    private bool _slepen, _sleepVerplaatst;
    private Point _sleepStart;
    private double _sleepX0, _sleepY0;
    private bool _pannen;
    private Point _panStart;
    private double _panH, _panV;
    private readonly Stack<string> _undo = new();

    public MainWindow()
    {
        InitializeComponent();
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _renderTimer.Tick += async (_, _) => { _renderTimer.Stop(); await RenderPaginaAsync(); };

        var p = new Project();
        p.Ramen.Add(RaamType.Standaard("Raam 1"));
        ZetProject(p, null);
        _gewijzigd = false;

        _klaar = true;
        UpdateReferentieUitleg();
        ZetModus(Modus.Selecteren);
        Loaded += MainWindow_Loaded;
    }

    /// <summary>Een .pdf of .raamproj die bij het starten is meegegeven (dubbelklik, slepen op de exe) direct openen.</summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length < 2 || !File.Exists(args[1])) return;
        string ext = IOPath.GetExtension(args[1]).ToLowerInvariant();
        if (ext == ".raamproj") await OpenProjectAsync(args[1]);
        else if (ext == ".pdf" && await LaadPdfAsync(args[1])) _project.PdfPad = args[1];
    }

    // =====================================================================
    // Project
    // =====================================================================

    private void ZetProject(Project p, string? pad)
    {
        _project = p;
        _projectPad = pad;
        HuidigProject = p;
        _undo.Clear();

        foreach (var r in p.Ramen) KoppelRaam(r);
        foreach (var pl in p.Plaatsingen) pl.PropertyChanged += Plaatsing_PropertyChanged;
        p.Ramen.CollectionChanged += (_, _) => { PlaatsingenLijst.Items.Refresh(); TekenOverlay(); };

        RamenLijst.ItemsSource = p.Ramen;
        PlaatsingRaamKeuze.ItemsSource = p.Ramen;
        PlaatsingenLijst.ItemsSource = p.Plaatsingen;
        RamenLijst.SelectedIndex = p.Ramen.Count > 0 ? 0 : -1;
        if (RamenLijst.SelectedIndex < 0) ToonRaam(null);
        PlaatsingenLijst.SelectedIndex = -1;
        ToonPlaatsing(null);
        TekenOverlay();
        UpdateTitel();
    }

    private void KoppelRaam(RaamType r)
    {
        r.Gewijzigd += Raam_Gewijzigd;
        r.PropertyChanged += Raam_PropertyChanged;
    }

    private void Raam_Gewijzigd(object? sender, EventArgs e)
    {
        MarkeerGewijzigd();
        if (sender == RamenLijst.SelectedItem)
        {
            TekenVoorbeeld();
            UpdateWaarschuwingen();
        }
        TekenOverlay();
        TekenHulp();
    }

    private void Raam_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RaamType.Naam))
        {
            Dispatcher.InvokeAsync(() =>
            {
                PlaatsingenLijst.Items.Refresh();
                UpdatePlaatsRaamNaam();
            });
        }
    }

    private void Plaatsing_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkeerGewijzigd();
        TekenOverlay();
        TekenHulp();
        if (e.PropertyName is nameof(Plaatsing.RaamId) or nameof(Plaatsing.Aanzicht) or nameof(Plaatsing.Pagina))
            Dispatcher.InvokeAsync(() => PlaatsingenLijst.Items.Refresh());
    }

    private void MarkeerGewijzigd()
    {
        if (_gewijzigd) return;
        _gewijzigd = true;
        UpdateTitel();
    }

    private void UpdateTitel()
    {
        string naam = _projectPad != null ? IOPath.GetFileNameWithoutExtension(_projectPad)
                    : _pdfPad != null ? IOPath.GetFileName(_pdfPad) : "nieuw";
        Title = $"Raam Tekenaar – {naam}{(_gewijzigd ? " *" : "")}";
    }

    private async void ProjectOpenen_Click(object sender, RoutedEventArgs e)
    {
        if (!VraagOpslaan()) return;
        var dlg = new OpenFileDialog { Filter = "Raam Tekenaar-project (*.raamproj)|*.raamproj" };
        if (dlg.ShowDialog(this) != true) return;
        await OpenProjectAsync(dlg.FileName);
    }

    private async Task OpenProjectAsync(string pad)
    {
        try
        {
            var p = ProjectOpslag.Laden(pad);
            ZetProject(p, pad);
            _gewijzigd = false;
            if (!string.IsNullOrEmpty(p.PdfPad) && File.Exists(p.PdfPad))
                await LaadPdfAsync(p.PdfPad);
            else
                MessageBox.Show(this, "De bijbehorende PDF is niet gevonden. Open de PDF via 📂 PDF openen.",
                    "Project openen", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateTitel();
        }
        catch (Exception ex) { Fout("Het project kon niet worden geopend.", ex); }
    }

    private void ProjectOpslaan_Click(object sender, RoutedEventArgs e) => ProjectOpslaan();

    private bool ProjectOpslaan()
    {
        string? pad = _projectPad;
        if (pad == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Raam Tekenaar-project (*.raamproj)|*.raamproj",
                FileName = _pdfPad != null ? IOPath.GetFileNameWithoutExtension(_pdfPad) + ".raamproj" : "ramen.raamproj",
                InitialDirectory = _pdfPad != null ? IOPath.GetDirectoryName(_pdfPad) : null
            };
            if (dlg.ShowDialog(this) != true) return false;
            pad = dlg.FileName;
        }
        try
        {
            _project.PdfPad = _pdfPad;
            ProjectOpslag.Opslaan(_project, pad);
            _projectPad = pad;
            _gewijzigd = false;
            UpdateTitel();
            ZetStatus("Project opgeslagen.");
            return true;
        }
        catch (Exception ex) { Fout("Het project kon niet worden opgeslagen.", ex); return false; }
    }

    /// <returns>false als de gebruiker annuleert.</returns>
    private bool VraagOpslaan()
    {
        if (!_gewijzigd) return true;
        var r = MessageBox.Show(this, "Wil je de wijzigingen in het project opslaan?", "Raam Tekenaar",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return ProjectOpslaan();
        return true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!VraagOpslaan()) e.Cancel = true;
    }

    // =====================================================================
    // PDF openen, tonen en exporteren
    // =====================================================================

    private async void PdfOpenen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF-bestanden (*.pdf)|*.pdf" };
        if (dlg.ShowDialog(this) != true) return;
        if (await LaadPdfAsync(dlg.FileName))
        {
            _project.PdfPad = dlg.FileName;
            if (_project.Plaatsingen.Count > 0) MarkeerGewijzigd();
        }
    }

    private async Task<bool> LaadPdfAsync(string pad)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var paginas = PdfBestand.LeesPaginas(pad);
            if (paginas.Count == 0) throw new InvalidOperationException("De PDF bevat geen pagina's.");
            var bestand = await WinStorage.StorageFile.GetFileFromPathAsync(pad);
            var doc = await WinPdf.PdfDocument.LoadFromFileAsync(bestand);

            _winDoc = doc;
            _paginas = paginas;
            _pdfPad = pad;
            _pagina = 0;
            LeegTekst.Visibility = Visibility.Collapsed;
            PaginaGrid.Visibility = Visibility.Visible;
            UpdateTitel();
            await ToonPaginaAsync(passend: true);
            return true;
        }
        catch (Exception ex)
        {
            Fout("De PDF kon niet worden geopend. Is het bestand beveiligd met een wachtwoord of beschadigd?", ex);
            return false;
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private async Task ToonPaginaAsync(bool passend)
    {
        if (_paginas.Count == 0) return;
        var info = _paginas[_pagina];
        PaginaGrid.Width = info.Breedte;
        PaginaGrid.Height = info.Hoogte;
        PaginaTekst.Text = $"Pagina {_pagina + 1} / {_paginas.Count}";
        PaginaAfbeelding.Source = null;
        if (passend)
        {
            Scroller.UpdateLayout();
            ZoomPassend();
        }
        TekenOverlay();
        TekenHulp();
        await RenderPaginaAsync();
    }

    private async Task RenderPaginaAsync()
    {
        if (_winDoc == null || _paginas.Count == 0) return;
        int versie = ++_renderVersie;
        var info = _paginas[_pagina];

        // Resolutie afstemmen op de zoomfactor, met een maximum voor het geheugen.
        double dpiSchaal = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double pxPerPt = Math.Clamp(_zoom * dpiSchaal * 1.25, 1.0, 8.0);
        double w = info.Breedte * pxPerPt, h = info.Hoogte * pxPerPt;
        double max = Math.Max(w, h);
        if (max > 9000) { w *= 9000 / max; h *= 9000 / max; }

        try
        {
            using var page = _winDoc.GetPage((uint)_pagina);
            using var stream = new WinStreams.InMemoryRandomAccessStream();
            var opties = new WinPdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(w)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(h)),
                BackgroundColor = new global::Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 }
            };
            await page.RenderToStreamAsync(stream, opties);
            if (versie != _renderVersie) return;

            stream.Seek(0);
            using var netStream = stream.AsStream();
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = netStream;
            bmp.EndInit();
            bmp.Freeze();
            PaginaAfbeelding.Source = bmp;
        }
        catch (Exception ex)
        {
            if (versie == _renderVersie) ZetStatus("Pagina kon niet worden weergegeven: " + ex.Message);
        }
    }

    private async void VorigePagina_Click(object sender, RoutedEventArgs e)
    {
        if (_pagina <= 0) return;
        _pagina--;
        PlaatsingenLijst.SelectedItem = null;
        await ToonPaginaAsync(passend: true);
    }

    private async void VolgendePagina_Click(object sender, RoutedEventArgs e)
    {
        if (_pagina >= _paginas.Count - 1) return;
        _pagina++;
        PlaatsingenLijst.SelectedItem = null;
        await ToonPaginaAsync(passend: true);
    }

    private void Exporteren_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfPad == null)
        {
            MessageBox.Show(this, "Open eerst een PDF-bouwtekening.", "PDF opslaan", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_project.Plaatsingen.Count == 0)
        {
            MessageBox.Show(this, "Er zijn nog geen ramen geplaatst.", "PDF opslaan", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Filter = "PDF-bestanden (*.pdf)|*.pdf",
            FileName = IOPath.GetFileNameWithoutExtension(_pdfPad) + "_ramen.pdf",
            InitialDirectory = IOPath.GetDirectoryName(_pdfPad)
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            int aantal = PdfBestand.Exporteer(_pdfPad, dlg.FileName, _project);
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, $"{aantal} raamaanzicht(en) getekend en opgeslagen als:\n{IOPath.GetFileName(dlg.FileName)}\n\nHet origineel is niet gewijzigd.",
                "PDF opgeslagen", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            Fout("De PDF kon niet worden opgeslagen. Staat het bestand nog open in een ander programma?", ex);
        }
    }

    // =====================================================================
    // Zoom en pannen
    // =====================================================================

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZetZoom(_zoom * 1.25, MiddenScroller());
    private void ZoomUit_Click(object sender, RoutedEventArgs e) => ZetZoom(_zoom / 1.25, MiddenScroller());
    private void ZoomPassend_Click(object sender, RoutedEventArgs e) => ZoomPassend();

    private Point MiddenScroller() => new(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);

    private void ZoomPassend()
    {
        if (_paginas.Count == 0) return;
        var info = _paginas[_pagina];
        double vb = (Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth) - 40;
        double vh = (Scroller.ViewportHeight > 0 ? Scroller.ViewportHeight : Scroller.ActualHeight) - 40;
        if (vb <= 0 || vh <= 0) return;
        ZetZoom(Math.Min(vb / info.Breedte, vh / info.Hoogte), null);
        Scroller.ScrollToHorizontalOffset(0);
        Scroller.ScrollToVerticalOffset(0);
    }

    private void ZetZoom(double nieuw, Point? rondScherm)
    {
        if (_paginas.Count == 0) return;
        nieuw = Math.Clamp(nieuw, 0.05, 50);
        Point? inhoud = rondScherm is Point s ? Scroller.TranslatePoint(s, PaginaGrid) : null;

        _zoom = nieuw;
        Zoom.ScaleX = nieuw;
        Zoom.ScaleY = nieuw;
        Scroller.UpdateLayout();

        if (inhoud is Point c && rondScherm is Point s2)
        {
            var nu = PaginaGrid.TranslatePoint(c, Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + nu.X - s2.X);
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + nu.Y - s2.Y);
        }

        TekenOverlay();
        TekenHulp();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_paginas.Count == 0) return;
        e.Handled = true;
        ZetZoom(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), e.GetPosition(Scroller));
    }

    private void Scroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Right) return;
        _pannen = true;
        _panStart = e.GetPosition(Scroller);
        _panH = Scroller.HorizontalOffset;
        _panV = Scroller.VerticalOffset;
        Scroller.CaptureMouse();
        Scroller.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pannen) return;
        var p = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(_panH - (p.X - _panStart.X));
        Scroller.ScrollToVerticalOffset(_panV - (p.Y - _panStart.Y));
        e.Handled = true;
    }

    private void Scroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pannen || (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Right)) return;
        _pannen = false;
        Scroller.ReleaseMouseCapture();
        Scroller.Cursor = null;
        e.Handled = true;
    }

    // =====================================================================
    // Muis op de tekening
    // =====================================================================

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_paginas.Count == 0) return;
        var p = e.GetPosition(Overlay);
        e.Handled = true;

        switch (_modus)
        {
            case Modus.Plaatsen:
                PlaatsRaam(p, blijfPlaatsen: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;

            case Modus.Kalibreren:
                if (_kalibratieStart is null)
                {
                    _kalibratieStart = p;
                    ZetStatus("Klik nu op het tweede punt.");
                    TekenHulp();
                }
                else
                {
                    var start = _kalibratieStart.Value;
                    _kalibratieStart = null;
                    RondKalibratieAf(start, p);
                }
                break;

            default:
                var raak = ZoekPlaatsing(p);
                PlaatsingenLijst.SelectedItem = raak;
                if (raak != null)
                {
                    BewaarUndo();
                    _slepen = true;
                    _sleepVerplaatst = false;
                    _sleepStart = p;
                    _sleepX0 = raak.X;
                    _sleepY0 = raak.Y;
                    Overlay.CaptureMouse();
                }
                break;
        }
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Overlay);
        _muis = p;
        PositieTekst.Text = $"x {p.X:0.0}  y {p.Y:0.0} pt   |   zoom {_zoom * 100:0}%";

        if (_slepen && PlaatsingenLijst.SelectedItem is Plaatsing pl)
        {
            double dx = p.X - _sleepStart.X, dy = p.Y - _sleepStart.Y;
            if (!_sleepVerplaatst && Math.Abs(dx) * _zoom < 3 && Math.Abs(dy) * _zoom < 3) return;
            _sleepVerplaatst = true;
            pl.X = _sleepX0 + dx;
            pl.Y = _sleepY0 + dy;
            return;
        }

        if (_modus != Modus.Selecteren) TekenHulp();
    }

    private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_slepen) return;
        _slepen = false;
        Overlay.ReleaseMouseCapture();
        if (!_sleepVerplaatst && _undo.Count > 0) _undo.Pop();
    }

    private void Overlay_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_slepen) return;
        _muis = null;
        TekenHulp();
    }

    private Plaatsing? ZoekPlaatsing(Point p)
    {
        double marge = 4 / _zoom;
        var ramen = _project.Ramen.ToDictionary(r => r.Id);
        foreach (var pl in _project.Plaatsingen.Reverse().Where(x => x.Pagina == _pagina))
        {
            if (!ramen.TryGetValue(pl.RaamId, out var raam)) continue;
            var pts = Plaatsingstransform.Bouw(pl, raam)
                .SelectMany(prim => prim.Punten)
                .Select(lp => Plaatsingstransform.NaarPagina(pl, lp))
                .ToList();
            if (pts.Count == 0) continue;
            if (p.X >= pts.Min(q => q.X) - marge && p.X <= pts.Max(q => q.X) + marge &&
                p.Y >= pts.Min(q => q.Y) - marge && p.Y <= pts.Max(q => q.Y) + marge)
                return pl;
        }
        return null;
    }

    // =====================================================================
    // Modus: plaatsen en kalibreren
    // =====================================================================

    private void ZetModus(Modus m)
    {
        _modus = m;
        _kalibratieStart = null;
        Overlay.Cursor = m == Modus.Selecteren ? Cursors.Arrow : Cursors.Cross;
        PlaatsKnop.Content = m == Modus.Plaatsen ? "✖ Stoppen met plaatsen (Esc)" : "➕ Plaats raam op tekening";
        ZetStatus(m switch
        {
            Modus.Plaatsen => "PLAATSEN – " + Labels.Referentie(NieuwAanzichtWaarde),
            Modus.Kalibreren => "KALIBREREN – Klik op het eerste punt van een bekende maat.",
            _ => _paginas.Count == 0
                ? "Open een PDF-bouwtekening om te beginnen."
                : "Klik op een raam om het te selecteren en te verslepen. Scrollwiel = zoomen, rechter- of middelste muisknop = schuiven."
        });
        TekenHulp();
    }

    private void Plaats_Click(object sender, RoutedEventArgs e)
    {
        if (_modus == Modus.Plaatsen) { ZetModus(Modus.Selecteren); return; }
        if (_paginas.Count == 0)
        {
            MessageBox.Show(this, "Open eerst een PDF-bouwtekening.", "Raam plaatsen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (RamenLijst.SelectedItem is not RaamType)
        {
            MessageBox.Show(this, "Kies eerst links een raamtype.", "Raam plaatsen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        LeesSchaalTekst();
        PlaatsingenLijst.SelectedItem = null;
        ZetModus(Modus.Plaatsen);
    }

    private Aanzicht NieuwAanzichtWaarde => NieuwAanzicht.SelectedValue is Aanzicht a ? a : Aanzicht.Voor;

    private Plaatsing NieuwePlaatsing(RaamType raam, Point p) => new()
    {
        RaamId = raam.Id,
        Pagina = _pagina,
        Aanzicht = NieuwAanzichtWaarde,
        X = p.X,
        Y = p.Y,
        Rotatie = NieuwRotatie.SelectedItem is int r ? r : 0,
        Gespiegeld = NieuwGespiegeld.IsChecked == true,
        Afdekken = NieuwAfdekken.IsChecked == true,
        Muurdikte = NieuwAanzichtWaarde == Aanzicht.Boven ? LeesGetal(NieuwMuurdikte.Text, 0) : 0,
        MmPerPt = _mmPerPt
    };

    private void PlaatsRaam(Point p, bool blijfPlaatsen)
    {
        if (RamenLijst.SelectedItem is not RaamType raam) return;
        BewaarUndo();
        var pl = NieuwePlaatsing(raam, p);
        pl.PropertyChanged += Plaatsing_PropertyChanged;
        _project.Plaatsingen.Add(pl);
        MarkeerGewijzigd();

        if (blijfPlaatsen)
        {
            TekenOverlay();
            TekenHulp();
            return;
        }
        ZetModus(Modus.Selecteren);
        PlaatsingenLijst.SelectedItem = pl;
        PlaatsingenLijst.ScrollIntoView(pl);
        ZetStatus($"{raam.Naam} geplaatst ({Labels.Kort(pl.Aanzicht)}aanzicht). Versleep het of gebruik de pijltjestoetsen om het precies te zetten.");
    }

    private void Kalibreren_Click(object sender, RoutedEventArgs e)
    {
        if (_paginas.Count == 0)
        {
            MessageBox.Show(this, "Open eerst een PDF-bouwtekening.", "Kalibreren", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ZetModus(Modus.Kalibreren);
    }

    private void RondKalibratieAf(Point a, Point b)
    {
        double afstandPt = (b - a).Length;
        ZetModus(Modus.Selecteren);
        if (afstandPt < 0.5)
        {
            ZetStatus("De twee punten liggen te dicht bij elkaar. Probeer het opnieuw.");
            return;
        }
        var invoer = InvoerDialoog.Vraag(this, "Kalibreren",
            "Wat is de werkelijke afstand tussen de twee aangeklikte punten (in mm)?", "1000");
        if (invoer == null) return;
        double mm = LeesGetal(invoer, -1);
        if (mm <= 0)
        {
            MessageBox.Show(this, "Vul een getal groter dan 0 in.", "Kalibreren", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _mmPerPt = mm / afstandPt;
        SchaalTekst.Text = Schaal.NaarGetal(_mmPerPt).ToString("0.##", CultureInfo.CurrentCulture);

        if (PlaatsingenLijst.SelectedItem is Plaatsing pl &&
            MessageBox.Show(this, "Deze schaal ook toepassen op het geselecteerde raam?", "Kalibreren",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            BewaarUndo();
            pl.MmPerPt = _mmPerPt;
        }
        ZetStatus($"Schaal ingesteld op 1:{SchaalTekst.Text}. Nieuwe ramen krijgen deze schaal.");
    }

    private void Schaal_LostFocus(object sender, RoutedEventArgs e) => LeesSchaalTekst();

    private void Schaal_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { LeesSchaalTekst(); Keyboard.ClearFocus(); }
    }

    private void LeesSchaalTekst()
    {
        double n = LeesGetal(SchaalTekst.Text, -1);
        if (n > 0) _mmPerPt = Schaal.NaarMmPerPt(n);
        else SchaalTekst.Text = Schaal.NaarGetal(_mmPerPt).ToString("0.##", CultureInfo.CurrentCulture);
        TekenHulp();
    }

    private void NieuwAanzicht_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_klaar) return;
        UpdateReferentieUitleg();
        if (_modus == Modus.Plaatsen) ZetModus(Modus.Plaatsen);
        TekenHulp();
    }

    private void NieuwInstelling_Changed(object sender, RoutedEventArgs e)
    {
        if (!_klaar) return;
        TekenHulp();
    }

    private void UpdateReferentieUitleg()
    {
        var a = NieuwAanzichtWaarde;
        string extra = a switch
        {
            Aanzicht.Voor => "",
            Aanzicht.Zij => "\nHet raam staat standaard rechts van de gevel (binnen = rechts). Gebruik Spiegelen als binnen links ligt.",
            _ => "\nStandaard ligt binnen boven de gevellijn. Gebruik Draaien en Spiegelen voor andere gevels."
        };
        ReferentieUitleg.Text = "Referentiepunt: " + Labels.Referentie(a) + extra;
        NieuwMuurdikte.IsEnabled = a == Aanzicht.Boven;
    }

    // =====================================================================
    // Raamtypes
    // =====================================================================

    private void RamenLijst_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ToonRaam(RamenLijst.SelectedItem as RaamType);

    private void ToonRaam(RaamType? r)
    {
        RaamEditor.DataContext = r;
        RaamEditor.IsEnabled = r != null;
        TekenVoorbeeld();
        UpdateWaarschuwingen();
        UpdatePlaatsRaamNaam();
        TekenHulp();
    }

    private void UpdatePlaatsRaamNaam() =>
        PlaatsRaamNaam.Text = RamenLijst.SelectedItem is RaamType r ? $"Raamtype: {r.Naam}" : "Kies links een raamtype.";

    private void RaamNieuw_Click(object sender, RoutedEventArgs e)
    {
        var r = RaamType.Standaard($"Raam {_project.Ramen.Count + 1}");
        KoppelRaam(r);
        _project.Ramen.Add(r);
        RamenLijst.SelectedItem = r;
        MarkeerGewijzigd();
    }

    private void RaamKopie_Click(object sender, RoutedEventArgs e)
    {
        if (RamenLijst.SelectedItem is not RaamType bron) return;
        var r = ProjectOpslag.Kopie(bron);
        KoppelRaam(r);
        _project.Ramen.Add(r);
        RamenLijst.SelectedItem = r;
        MarkeerGewijzigd();
    }

    private void RaamVerwijder_Click(object sender, RoutedEventArgs e)
    {
        if (RamenLijst.SelectedItem is not RaamType r) return;
        int gebruikt = _project.Plaatsingen.Count(p => p.RaamId == r.Id);
        string vraag = gebruikt > 0
            ? $"{r.Naam} is {gebruikt}× geplaatst. Raamtype én deze plaatsingen verwijderen?"
            : $"{r.Naam} verwijderen?";
        if (MessageBox.Show(this, vraag, "Verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var p in _project.Plaatsingen.Where(p => p.RaamId == r.Id).ToList())
        {
            p.PropertyChanged -= Plaatsing_PropertyChanged;
            _project.Plaatsingen.Remove(p);
        }
        _undo.Clear(); // undo zou anders naar een verwijderd raamtype verwijzen
        r.Gewijzigd -= Raam_Gewijzigd;
        r.PropertyChanged -= Raam_PropertyChanged;
        _project.Ramen.Remove(r);
        RamenLijst.SelectedIndex = _project.Ramen.Count > 0 ? 0 : -1;
        MarkeerGewijzigd();
        TekenOverlay();
    }

    private void VakToevoegen_Click(object sender, RoutedEventArgs e)
    {
        if (RamenLijst.SelectedItem is RaamType r) r.Vakken.Add(new Vak { Type = VakType.Vast });
    }

    private void VakVerwijderen_Click(object sender, RoutedEventArgs e)
    {
        if (RamenLijst.SelectedItem is not RaamType r || r.Vakken.Count == 0) return;
        VakkenGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var vak = VakkenGrid.SelectedItem as Vak ?? r.Vakken[^1];
        r.Vakken.Remove(vak);
    }

    private void UpdateWaarschuwingen()
    {
        Waarschuwingen.Text = RamenLijst.SelectedItem is RaamType r
            ? string.Join("\n", RaamGeometrie.Controleer(r).Select(m => "⚠ " + m))
            : "";
    }

    private void Voorbeeld_SizeChanged(object sender, SizeChangedEventArgs e) => TekenVoorbeeld();

    private void TekenVoorbeeld()
    {
        Voorbeeld.Children.Clear();
        if (RamenLijst.SelectedItem is not RaamType r) return;
        double cw = Voorbeeld.ActualWidth, ch = Voorbeeld.ActualHeight;
        if (cw < 20 || ch < 20) return;

        var prims = RaamGeometrie.Bouw(r, Aanzicht.Voor, false, 0, 0);
        var alle = prims.SelectMany(p => p.Punten).ToList();
        if (alle.Count == 0) return;
        double minX = alle.Min(p => p.X), maxX = alle.Max(p => p.X);
        double minY = Math.Min(0, alle.Min(p => p.Y)), maxY = alle.Max(p => p.Y);
        double marge = 12;
        double s = Math.Min((cw - 2 * marge) / Math.Max(maxX - minX, 1), (ch - 2 * marge) / Math.Max(maxY - minY, 1));
        Point Naar(Pt q) => new(marge + (q.X - minX) * s, ch - marge - (q.Y - minY) * s);

        // vloerpeil
        var vloer = new ShapeLine
        {
            X1 = 2, X2 = cw - 2,
            Y1 = Naar(new Pt(0, 0)).Y, Y2 = Naar(new Pt(0, 0)).Y,
            Stroke = Brushes.Gray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }
        };
        Voorbeeld.Children.Add(vloer);

        foreach (var prim in prims)
        {
            var pad = MaakPad(prim, Naar, Brushes.Black, Math.Max(prim.DikteMm * 3, 0.8), prim.Gestreept ? 3.0 : 0);
            if (pad != null) Voorbeeld.Children.Add(pad);
        }
    }

    // =====================================================================
    // Plaatsingen
    // =====================================================================

    private void PlaatsingenLijst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var pl = PlaatsingenLijst.SelectedItem as Plaatsing;
        ToonPlaatsing(pl);
        if (pl != null && pl.Pagina != _pagina && pl.Pagina < _paginas.Count)
        {
            _pagina = pl.Pagina;
            _ = ToonPaginaAsync(passend: true);
        }
        TekenOverlay();
        TekenHulp();
    }

    private void ToonPlaatsing(Plaatsing? pl)
    {
        PlaatsingEditor.DataContext = pl;
        PlaatsingEditor.IsEnabled = pl != null;
    }

    private void PlaatsingVerwijderen_Click(object sender, RoutedEventArgs e) => VerwijderGeselecteerde();

    private void VerwijderGeselecteerde()
    {
        if (PlaatsingenLijst.SelectedItem is not Plaatsing pl) return;
        BewaarUndo();
        pl.PropertyChanged -= Plaatsing_PropertyChanged;
        _project.Plaatsingen.Remove(pl);
        MarkeerGewijzigd();
        TekenOverlay();
        TekenHulp();
    }

    private void Verschuif_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string richting }) Verschuif(richting, 1);
    }

    private void Verschuif(string richting, double factor)
    {
        if (PlaatsingenLijst.SelectedItem is not Plaatsing pl) return;
        double stapMm = LeesGetal(StapTekst.Text, 10) * factor;
        double stapPt = stapMm / pl.MmPerPt;
        BewaarUndo();
        switch (richting)
        {
            case "L": pl.X -= stapPt; break;
            case "R": pl.X += stapPt; break;
            case "U": pl.Y -= stapPt; break;
            case "D": pl.Y += stapPt; break;
        }
    }

    // =====================================================================
    // Ongedaan maken (alleen plaatsingen)
    // =====================================================================

    private void BewaarUndo()
    {
        _undo.Push(ProjectOpslag.PlaatsingenNaarJson(_project.Plaatsingen));
    }

    private void Undo()
    {
        if (_undo.Count == 0) { ZetStatus("Niets meer om ongedaan te maken."); return; }
        var lijst = ProjectOpslag.PlaatsingenVanJson(_undo.Pop());
        Guid? sel = (PlaatsingenLijst.SelectedItem as Plaatsing)?.Id;

        foreach (var p in _project.Plaatsingen) p.PropertyChanged -= Plaatsing_PropertyChanged;
        _project.Plaatsingen.Clear();
        foreach (var p in lijst)
        {
            p.PropertyChanged += Plaatsing_PropertyChanged;
            _project.Plaatsingen.Add(p);
        }
        PlaatsingenLijst.SelectedItem = _project.Plaatsingen.FirstOrDefault(p => p.Id == sel);
        MarkeerGewijzigd();
        TekenOverlay();
        TekenHulp();
        ZetStatus("Ongedaan gemaakt.");
    }

    // =====================================================================
    // Toetsenbord
    // =====================================================================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.S) { ProjectOpslaan(); e.Handled = true; return; }

        // Niet ingrijpen als er in een invoerveld of tabel wordt getypt
        if (Keyboard.FocusedElement is TextBox or ComboBox or DataGridCell or ComboBoxItem) return;

        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                ZetModus(Modus.Selecteren);
                e.Handled = true;
                break;
            case Key.Delete:
                VerwijderGeselecteerde();
                e.Handled = true;
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when PlaatsingenLijst.SelectedItem is Plaatsing:
                double factor = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                Verschuif(e.Key switch { Key.Left => "L", Key.Right => "R", Key.Up => "U", _ => "D" }, factor);
                e.Handled = true;
                break;
        }
    }

    // =====================================================================
    // Tekenen op de tekening
    // =====================================================================

    private void TekenOverlay()
    {
        Overlay.Children.Clear();
        if (_paginas.Count == 0) return;
        var ramen = _project.Ramen.ToDictionary(r => r.Id);
        var geselecteerd = PlaatsingenLijst.SelectedItem as Plaatsing;

        foreach (var pl in _project.Plaatsingen.Where(p => p.Pagina == _pagina))
        {
            if (!ramen.TryGetValue(pl.RaamId, out var raam)) continue;
            TekenPlaatsing(Overlay, pl, raam, pl == geselecteerd ? KleurGeselecteerd : KleurNormaal);
        }
    }

    private void TekenHulp()
    {
        if (!_klaar) return;
        Hulplaag.Children.Clear();
        if (_paginas.Count == 0) return;

        if (PlaatsingenLijst.SelectedItem is Plaatsing sel && sel.Pagina == _pagina)
            TekenMarker(new Point(sel.X, sel.Y));

        if (_modus == Modus.Plaatsen && _muis is Point m && RamenLijst.SelectedItem is RaamType raam)
        {
            var spook = NieuwePlaatsing(raam, m);
            TekenPlaatsing(Hulplaag, spook, raam, KleurSpook, 0.75);
            TekenMarker(m);
        }

        if (_modus == Modus.Kalibreren && _kalibratieStart is Point a)
        {
            TekenMarker(a);
            if (_muis is Point b)
            {
                Hulplaag.Children.Add(new ShapeLine
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = KleurMarker, StrokeThickness = 1.5 / _zoom,
                    StrokeDashArray = new DoubleCollection { 4, 3 }
                });
            }
        }
    }

    private void TekenMarker(Point p)
    {
        double r = 7 / _zoom, d = 1.5 / _zoom;
        Hulplaag.Children.Add(new ShapeLine { X1 = p.X - r, Y1 = p.Y, X2 = p.X + r, Y2 = p.Y, Stroke = KleurMarker, StrokeThickness = d });
        Hulplaag.Children.Add(new ShapeLine { X1 = p.X, Y1 = p.Y - r, X2 = p.X, Y2 = p.Y + r, Stroke = KleurMarker, StrokeThickness = d });
    }

    private void TekenPlaatsing(Canvas doel, Plaatsing pl, RaamType raam, Brush kleur, double doorzichtigheid = 1)
    {
        Point Naar(Pt lp)
        {
            var q = Plaatsingstransform.NaarPagina(pl, lp);
            return new Point(q.X, q.Y);
        }

        double minDikte = 0.8 / _zoom; // altijd minstens ~1 schermpixel zichtbaar
        foreach (var prim in Plaatsingstransform.Bouw(pl, raam))
        {
            double dikte = Math.Max(Schaal.PapierMmNaarPt(prim.DikteMm), minDikte);
            double streep = prim.Gestreept ? Schaal.PapierMmNaarPt(1.5) : 0;
            var pad = MaakPad(prim, Naar, kleur, dikte, streep);
            if (pad == null) continue;
            pad.Opacity = doorzichtigheid;
            doel.Children.Add(pad);
        }
    }

    /// <param name="streepLengte">Lengte van een streepje in dezelfde eenheid als de dikte; 0 = doorgetrokken.</param>
    private static ShapePath? MaakPad(Prim prim, Func<Pt, Point> naar, Brush kleur, double dikte, double streepLengte)
    {
        if (prim.Punten.Count < 2) return null;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(naar(prim.Punten[0]), prim.WitVullen, prim.Gesloten);
            ctx.PolyLineTo(prim.Punten.Skip(1).Select(naar).ToList(), prim.Lijn, true);
        }
        geo.Freeze();

        var pad = new ShapePath { Data = geo, IsHitTestVisible = false };
        if (prim.WitVullen) pad.Fill = Brushes.White;
        if (prim.Lijn)
        {
            pad.Stroke = kleur;
            pad.StrokeThickness = dikte;
            pad.StrokeLineJoin = PenLineJoin.Round;
            pad.StrokeStartLineCap = PenLineCap.Round;
            pad.StrokeEndLineCap = PenLineCap.Round;
            if (streepLengte > 0 && dikte > 0)
            {
                // StrokeDashArray is in veelvouden van de lijndikte; ronde kapjes verlengen elk streepje met één dikte.
                double aan = Math.Max(streepLengte / dikte - 1, 0.1);
                double uit = streepLengte * (2.0 / 3.0) / dikte + 1;
                pad.StrokeDashArray = new DoubleCollection { aan, uit };
                pad.StrokeDashCap = PenLineCap.Round;
            }
        }
        return pad;
    }

    // =====================================================================
    // Hulpjes
    // =====================================================================

    private void ZetStatus(string tekst) => StatusTekst.Text = tekst;

    private void Fout(string melding, Exception ex) =>
        MessageBox.Show(this, melding + "\n\n" + ex.Message, "Raam Tekenaar", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>Leest een getal; accepteert zowel komma als punt.</summary>
    private static double LeesGetal(string? tekst, double standaard)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return standaard;
        tekst = tekst.Trim().Replace(',', '.');
        return double.TryParse(tekst, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : standaard;
    }

    private static Brush Bevroren(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
