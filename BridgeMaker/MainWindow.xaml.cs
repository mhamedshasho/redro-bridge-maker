using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RedroBridgeMaker;

public partial class MainWindow : Window
{
    readonly DocumentModel document = new();
    double baseScale = 1, scale = 1, minX, maxY;
    PathModel? selectedPath;
    bool updatingPathSelector;
    bool manualMode;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await UpdateService.CheckAndOfferAsync(this);
    }

    void Draw()
    {
        Preview.Children.Clear();
        if (!document.Paths.Any())
        {
            Dimensions.Text = "Drawing dimensions / أبعاد الرسم: —";
            Status.Text = "No vector paths loaded / لا توجد مسارات.";
            return;
        }
        var points = document.Paths.SelectMany(p => p.Points).ToList();
        minX = points.Min(p => p.X); var minY = points.Min(p => p.Y); maxY = points.Max(p => p.Y);
        var maxX = points.Max(p => p.X); var width = Math.Max(0, maxX - minX); var height = Math.Max(0, maxY - minY);
        baseScale = Math.Min(1000 / Math.Max(1, width), 620 / Math.Max(1, height));
        scale = baseScale * ZoomSlider.Value;

        foreach (var path in document.Paths)
            foreach (var segment in path.Segments)
                foreach (var visible in BridgeEngine.VisibleParts(segment, document.Gaps))
                    Preview.Children.Add(new Line { X1 = (visible.A.X - minX) * scale, Y1 = (maxY - visible.A.Y) * scale, X2 = (visible.B.X - minX) * scale, Y2 = (maxY - visible.B.Y) * scale, Stroke = path == selectedPath ? Brushes.DodgerBlue : Brushes.Black, StrokeThickness = path == selectedPath ? Math.Max(2, 2 * ZoomSlider.Value) : Math.Max(1, 1.2 * ZoomSlider.Value) });
        Preview.Width = Math.Max(100, width * scale + 20); Preview.Height = Math.Max(100, height * scale + 20);

        var totalLength = document.Paths.Sum(BridgeEngine.PathLength);
        var text = $"Drawing / الرسم: W {width / 10:0.##} cm × H {height / 10:0.##} cm | Total path / طول المسارات: {totalLength / 10:0.##} cm";
        if (selectedPath != null)
        {
            var selectedBounds = Bounds(selectedPath.Points);
            text += $" | Selected path / المسار المحدد: L {BridgeEngine.PathLength(selectedPath) / 10:0.##} cm, W {selectedBounds.Width / 10:0.##} cm × H {selectedBounds.Height / 10:0.##} cm";
        }
        Dimensions.Text = text;
        Status.Text = $"Paths / المسارات: {document.Paths.Count} | Bridges / الجسور: {document.BridgeCount} | Zoom / التكبير: {ZoomSlider.Value * 100:0}%";
    }

    void PopulatePathSelector()
    {
        updatingPathSelector = true;
        PathSelector.Items.Clear();
        PathSelector.Items.Add("All drawing / كامل الرسم");
        for (var i = 0; i < document.Paths.Count; i++) PathSelector.Items.Add($"Path {i + 1} / المسار {i + 1}");
        PathSelector.SelectedIndex = selectedPath == null ? 0 : document.Paths.IndexOf(selectedPath) + 1;
        updatingPathSelector = false;
    }

    void PathSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingPathSelector || PathSelector.SelectedIndex < 0) return;
        selectedPath = PathSelector.SelectedIndex == 0 ? null : document.Paths[PathSelector.SelectedIndex - 1];
        Draw();
    }

    void SelectPath(PathModel path)
    {
        selectedPath = path;
        updatingPathSelector = true; PathSelector.SelectedIndex = document.Paths.IndexOf(path) + 1; updatingPathSelector = false;
        Draw();
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Vector files|*.cdr;*.pdf;*.eps;*.ps;*.svg|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var loaded = VectorParser.Load(dialog.FileName);
            document.Paths.Clear(); document.Gaps.Clear(); document.BridgeCount = 0; document.Paths.AddRange(loaded.Paths); selectedPath = null; PopulatePathSelector(); Draw();
            manualMode = false; ManualButton.Content = "Manual: Off | يدوي: إيقاف";
            Status.Text = $"Loaded / تم فتح: {System.IO.Path.GetFileName(dialog.FileName)} | Paths / المسارات: {document.Paths.Count}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open failed / فشل الفتح", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void Auto_Click(object sender, RoutedEventArgs e)
    {
        if (!document.Paths.Any()) { MessageBox.Show("Open a vector file first / افتح ملفاً أولاً."); return; }
        if (!TryReadSettings(out var length, out var start, out var end, out var spacing, out var count)) return;
        try
        {
            document.Gaps.Clear(); document.BridgeCount = 0;
            var placed = BridgeEngine.Automatic(document, length, start, end, spacing, count);
            Draw(); Status.Text += $" | Placed / تم وضع: {placed}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Automatic bridge failed / فشل الجسور التلقائية", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void Manual_Click(object sender, RoutedEventArgs e)
    {
        if (!document.Paths.Any()) { MessageBox.Show("Open a vector file first / افتح ملفاً أولاً."); return; }
        manualMode = !manualMode;
        ManualButton.Content = manualMode ? "Manual: On (click path) | يدوي: اضغط المسار" : "Manual: Off | يدوي: إيقاف";
        Status.Text = manualMode ? "Click a path / اضغط على المسار لفتح إعدادات الجسر." : "Manual mode is off / الوضع اليدوي متوقف.";
    }

    void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!manualMode) return;
        var screen = e.GetPosition(Preview);
        var target = new Pt(minX + screen.X / scale, maxY - screen.Y / scale);
        PathModel? path = null; var nearestDistance = double.MaxValue;
        foreach (var candidate in document.Paths)
            if (BridgeEngine.TryGetNearest(candidate, target, out _, out var distance) && distance < nearestDistance) { path = candidate; nearestDistance = distance; }
        if (path == null || nearestDistance > 8 / scale) { Status.Text = "Click directly on a black path / اضغط مباشرة على مسار أسود."; return; }
        SelectPath(path);
        if (!TryReadLength(out var length)) return;
        if (!ShowPlacementDialog(this, ReadOrDefault(StartOffset.Text, 2), ReadOrDefault(EndOffset.Text, 2), ReadOrDefault(Spacing.Text, 100), out var start, out var end, out var count, out var spacing)) return;
        var newGaps = new List<Gap>();
        var placed = BridgeEngine.PlaceSeries(path, newGaps, length, start, end, spacing, count);
        if (placed != count) { MessageBox.Show($"Only {placed} bridge(s) fit / عدد الجسور الممكنة هو {placed}.", "Not enough path length / طول المسار غير كافٍ", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        document.Gaps.AddRange(newGaps); document.BridgeCount += placed; Draw();
    }

    void Clear_Click(object sender, RoutedEventArgs e) { document.Gaps.Clear(); document.BridgeCount = 0; Draw(); }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!document.Paths.Any()) { MessageBox.Show("Open a vector file first / افتح ملفاً أولاً."); return; }
        var dialog = new SaveFileDialog { Filter = "SVG file|*.svg", FileName = "bridged-output.svg" };
        if (dialog.ShowDialog() != true) return;
        try { SvgExporter.Save(document, dialog.FileName); Status.Text = "Exported successfully / تم التصدير بنجاح."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed / فشل التصدير", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ZoomText != null) ZoomText.Text = $"{e.NewValue * 100:0}%"; if (IsLoaded) Draw(); }
    void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 0.25);
    void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 0.25);
    void Preview_MouseWheel(object sender, MouseWheelEventArgs e) { e.Handled = true; if (e.Delta > 0) ZoomIn_Click(sender, e); else ZoomOut_Click(sender, e); }

    bool TryReadSettings(out double length, out double start, out double end, out double spacing, out int count)
    {
        length = start = end = spacing = 0; count = 0;
        if (!TryReadLength(out length) || !TryRead(StartOffset.Text, "start offset / مسافة البداية", out start) || !TryRead(EndOffset.Text, "end offset / مسافة النهاية", out end) || !TryRead(Spacing.Text, "spacing / المسافة", out spacing) || !TryReadInt(BridgeCount.Text, "bridge count / عدد الجسور", out count)) return false;
        if (start < 0 || end < 0 || spacing <= 0 || count < 0) { MessageBox.Show("Offsets/count cannot be negative and spacing must be greater than zero.\nلا يمكن أن تكون القيم سالبة والمسافة يجب أن تكون أكبر من صفر."); return false; }
        return true;
    }

    bool TryReadLength(out double value) { value = 0; if (!TryRead(BridgeLength.Text, "bridge length / طول الجسر", out value) || value <= 0) { MessageBox.Show("Bridge length must be greater than zero / طول الجسر يجب أن يكون أكبر من صفر."); return false; } return true; }
    static bool TryRead(string text, string label, out double value) { if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cm) && !double.IsNaN(cm) && !double.IsInfinity(cm)) { value = cm * 10; return true; } value = 0; MessageBox.Show($"Enter a valid {label} in centimetres / أدخل قيمة صحيحة بالسنتيمتر."); return false; }
    static bool TryReadInt(string text, string label, out int value) { if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true; value = 0; MessageBox.Show($"Enter a valid whole number for {label} / أدخل رقماً صحيحاً لـ {label}."); return false; }
    static double ReadOrDefault(string text, double fallback) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value * 10 : fallback * 10;

    static (double Width, double Height) Bounds(IEnumerable<Pt> points)
    {
        var list = points.ToList(); if (list.Count == 0) return (0, 0);
        return (list.Max(p => p.X) - list.Min(p => p.X), list.Max(p => p.Y) - list.Min(p => p.Y));
    }

    static bool ShowPlacementDialog(Window owner, double startDefault, double endDefault, double spacingDefault, out double start, out double end, out int count, out double spacing)
    {
        start = end = spacing = 0; count = 0;
        var window = new Window { Title = "Manual bridge settings / إعدادات الجسر اليدوي", Width = 430, Height = 330, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner };
        var grid = new Grid { Margin = new Thickness(12) };
        for (var i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var fields = new[] { ("Start from beginning / من بداية المسار (cm)", (startDefault / 10).ToString("0.##", CultureInfo.InvariantCulture)), ("End from end / من نهاية المسار (cm)", (endDefault / 10).ToString("0.##", CultureInfo.InvariantCulture)), ("Number of bridges / عدد الجسور", "1"), ("Spacing between starts / المسافة بين البدايات (cm)", (spacingDefault / 10).ToString("0.##", CultureInfo.InvariantCulture)) };
        var boxes = new List<TextBox>();
        for (var i = 0; i < fields.Length; i++) { var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) }; panel.Children.Add(new TextBlock { Text = fields[i].Item1, Width = 265, VerticalAlignment = VerticalAlignment.Center }); var box = new TextBox { Text = fields[i].Item2, Width = 100 }; boxes.Add(box); panel.Children.Add(box); Grid.SetRow(panel, i); grid.Children.Add(panel); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var ok = new Button { Content = "OK / موافق", Width = 90, IsDefault = true }; var cancel = new Button { Content = "Cancel / إلغاء", Width = 90, IsCancel = true }; buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetRow(buttons, 5); grid.Children.Add(buttons); window.Content = grid;
        double parsedStart = 0, parsedEnd = 0, parsedSpacing = 0; int parsedCount = 0;
        ok.Click += (_, _) => { if (TryParseDialog(boxes, out parsedStart, out parsedEnd, out parsedCount, out parsedSpacing)) window.DialogResult = true; };
        if (window.ShowDialog() != true) return false;
        start = parsedStart * 10; end = parsedEnd * 10; count = parsedCount; spacing = parsedSpacing * 10; return true;
    }

    static bool TryParseDialog(List<TextBox> boxes, out double start, out double end, out int count, out double spacing)
    {
        start = end = spacing = 0; count = 0;
        if (!double.TryParse(boxes[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out start) || !double.TryParse(boxes[1].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out end) || !int.TryParse(boxes[2].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || !double.TryParse(boxes[3].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out spacing) || start < 0 || end < 0 || count <= 0 || spacing <= 0) { MessageBox.Show("Use valid positive values / استخدم قيماً صحيحة وموجبة."); return false; }
        return true;
    }
}
