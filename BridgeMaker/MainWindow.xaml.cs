using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RedroBridgeMaker;

public partial class MainWindow : Window
{
    readonly DocumentModel document = new();
    double scale = 1;
    double minX, maxY;
    bool manualMode;

    public MainWindow() => InitializeComponent();

    void Draw()
    {
        Preview.Children.Clear();
        if (!document.Paths.Any()) { Status.Text = "No vector paths loaded."; return; }
        var points = document.Paths.SelectMany(p => p.Points).ToList();
        minX = points.Min(p => p.X); var minY = points.Min(p => p.Y); maxY = points.Max(p => p.Y);
        var maxX = points.Max(p => p.X); var width = Math.Max(1, maxX - minX); var height = Math.Max(1, maxY - minY);
        scale = Math.Min(1000 / width, 620 / height);
        foreach (var path in document.Paths)
            foreach (var segment in path.Segments)
                Preview.Children.Add(new Line { X1 = (segment.A.X - minX) * scale, Y1 = (maxY - segment.A.Y) * scale, X2 = (segment.B.X - minX) * scale, Y2 = (maxY - segment.B.Y) * scale, Stroke = Brushes.Black, StrokeThickness = 1 });
        foreach (var gap in document.Gaps)
            Preview.Children.Add(new Line { X1 = (gap.A.X - minX) * scale, Y1 = (maxY - gap.A.Y) * scale, X2 = (gap.B.X - minX) * scale, Y2 = (maxY - gap.B.Y) * scale, Stroke = Brushes.Red, StrokeThickness = 4 });
        Preview.Width = width * scale + 20; Preview.Height = height * scale + 20;
        Status.Text = $"Paths: {document.Paths.Count} | Bridges: {document.Gaps.Count} | Units: mm";
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Vector files|*.cdr;*.pdf;*.eps;*.ps;*.svg|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var loaded = VectorParser.Load(dialog.FileName);
            document.Paths.Clear(); document.Gaps.Clear(); document.Paths.AddRange(loaded.Paths);
            manualMode = false; ManualButton.Content = "Manual Bridge: Off"; Draw();
            Status.Text = $"Loaded {System.IO.Path.GetFileName(dialog.FileName)} | Paths: {document.Paths.Count} | Units: mm";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void Auto_Click(object sender, RoutedEventArgs e)
    {
        if (!document.Paths.Any()) { MessageBox.Show("Open a vector file first."); return; }
        if (!TryReadSettings(out var length, out var start, out var end, out var spacing)) return;
        try { document.Gaps.Clear(); BridgeEngine.Automatic(document, length, start, end, spacing); Draw(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Automatic bridge failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void Manual_Click(object sender, RoutedEventArgs e)
    {
        if (!document.Paths.Any()) { MessageBox.Show("Open a vector file first."); return; }
        if (!TryReadLength(out _)) return;
        manualMode = !manualMode;
        ManualButton.Content = manualMode ? "Manual Bridge: On (click path)" : "Manual Bridge: Off";
        Status.Text = manualMode ? "Manual mode: click any path to place a bridge." : "Manual mode is off.";
    }

    void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!manualMode || !TryReadLength(out var length)) return;
        var screen = e.GetPosition(Preview);
        var target = new Pt(minX + screen.X / scale, maxY - screen.Y / scale);
        PathModel? bestPath = null; var bestDistance = double.MaxValue; var bestAlong = 0.0;
        foreach (var path in document.Paths)
            if (BridgeEngine.TryGetNearest(path, target, out var along, out var distance) && distance < bestDistance) { bestPath = path; bestDistance = distance; bestAlong = along; }
        if (bestPath == null || bestDistance > 8 / scale) { Status.Text = "Click directly on a black path to place a bridge."; return; }
        if (BridgeEngine.AddAt(bestPath, document.Gaps, bestAlong - length / 2, length) == 0)
        { Status.Text = "The bridge does not fit at that location; click farther from the path end."; return; }
        Draw(); Status.Text += " | Manual bridge added.";
    }

    void Clear_Click(object sender, RoutedEventArgs e) { document.Gaps.Clear(); Draw(); }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!document.Paths.Any()) { MessageBox.Show("Open a vector file first."); return; }
        var dialog = new SaveFileDialog { Filter = "SVG file|*.svg", FileName = "bridged-output.svg" };
        if (dialog.ShowDialog() != true) return;
        try { SvgExporter.Save(document, dialog.FileName); Status.Text = "Exported successfully."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    bool TryReadSettings(out double length, out double start, out double end, out double spacing)
    {
        length = start = end = spacing = 0;
        if (!TryReadLength(out length) || !TryRead(StartOffset.Text, "start offset", out start) || !TryRead(EndOffset.Text, "end offset", out end) || !TryRead(Spacing.Text, "spacing", out spacing)) return false;
        if (start < 0 || end < 0 || spacing <= 0) { MessageBox.Show("Start/end offsets cannot be negative and spacing must be greater than zero."); return false; }
        return true;
    }

    bool TryReadLength(out double value)
    {
        value = 0;
        if (!TryRead(BridgeLength.Text, "bridge length", out value) || value <= 0) { MessageBox.Show("Bridge length must be greater than zero."); return false; }
        return true;
    }

    static bool TryRead(string text, string label, out double value)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value)) { MessageBox.Show($"Enter a valid {label} in millimetres."); return false; }
        return true;
    }
}
