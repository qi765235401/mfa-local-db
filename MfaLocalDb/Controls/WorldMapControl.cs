using System.Drawing.Drawing2D;
using System.Text.Json;
using MfaLocalDb;

namespace MfaLocalDb.Controls;

public sealed class WorldMapControl : Control
{
    private readonly List<MapCountry> _countries = [];
    private readonly StringComparer _nameComparer = StringComparer.Ordinal;
    private HashSet<string> _availableCountryNames = new(StringComparer.Ordinal);
    private string? _hoverCountryName;
    private Point _hoverLocation;
    private float _zoom = 1f;
    private PointF _panOffset;
    private bool _isPanning;
    private Point _lastMouseLocation;

    public WorldMapControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(246, 249, 252);
        Cursor = Cursors.Hand;
        LoadCountries();
    }

    public event EventHandler<string>? CountrySelected;

    public void SetAvailableCountries(IReadOnlySet<string> countryNames)
    {
        _availableCountryNames = new HashSet<string>(countryNames, StringComparer.Ordinal);
        Invalidate();
    }

    public void ResetView()
    {
        _zoom = 1f;
        _panOffset = PointF.Empty;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isPanning)
        {
            _panOffset.X += e.X - _lastMouseLocation.X;
            _panOffset.Y += e.Y - _lastMouseLocation.Y;
            _lastMouseLocation = e.Location;
            Invalidate();
            return;
        }

        base.OnMouseMove(e);
        _hoverLocation = e.Location;
        var country = HitTest(e.Location);
        var name = country?.ChineseName;
        if (!_nameComparer.Equals(_hoverCountryName, name))
        {
            _hoverCountryName = name;
            Invalidate();
        }

        Cursor = country is not null && IsAvailable(country.ChineseName) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button is MouseButtons.Right or MouseButtons.Middle)
        {
            _isPanning = true;
            _lastMouseLocation = e.Location;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isPanning = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var oldBounds = GetMapBounds();
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.18f : 0.85f), 1f, 12f);
        if (Math.Abs(_zoom - oldZoom) < 0.001f)
        {
            return;
        }

        var newBounds = GetMapBounds();
        var ratioX = oldBounds.Width <= 0 ? 0.5f : (e.X - oldBounds.Left) / oldBounds.Width;
        var ratioY = oldBounds.Height <= 0 ? 0.5f : (e.Y - oldBounds.Top) / oldBounds.Height;
        _panOffset.X += e.X - (newBounds.Left + ratioX * newBounds.Width);
        _panOffset.Y += e.Y - (newBounds.Top + ratioY * newBounds.Height);
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var country = HitTest(e.Location);
        if (country is not null && IsAvailable(country.ChineseName))
        {
            CountrySelected?.Invoke(this, country.ChineseName);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverCountryName = null;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        var mapBounds = GetMapBounds();
        using var oceanBrush = new SolidBrush(Color.FromArgb(236, 242, 248));
        using var borderPen = new Pen(Color.FromArgb(214, 224, 235), 1f);
        e.Graphics.FillRectangle(oceanBrush, mapBounds);
        e.Graphics.DrawRectangle(borderPen, mapBounds);

        using var disabledBrush = new SolidBrush(Color.FromArgb(222, 227, 233));
        using var availableBrush = new SolidBrush(Color.FromArgb(186, 210, 238));
        using var coastPen = new Pen(Color.White, 0.8f);

        foreach (var country in _countries)
        {
            var isAvailable = IsAvailable(country.ChineseName);
            using var path = BuildPath(country, mapBounds);
            e.Graphics.FillPath(isAvailable ? availableBrush : disabledBrush, path);
            e.Graphics.DrawPath(coastPen, path);
        }

        var hoverCountry = !string.IsNullOrWhiteSpace(_hoverCountryName)
            ? _countries.FirstOrDefault(country => _nameComparer.Equals(country.ChineseName, _hoverCountryName))
            : null;
        if (hoverCountry is not null)
        {
            DrawHoverBoundary(e.Graphics, hoverCountry, mapBounds);
            DrawTooltip(e.Graphics, hoverCountry.ChineseName);
        }
    }

    private void DrawHoverBoundary(Graphics graphics, MapCountry country, RectangleF mapBounds)
    {
        using var path = BuildPath(country, mapBounds);
        using var glowPen = new Pen(Color.FromArgb(235, 255, 255, 255), 7f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var outlinePen = new Pen(Color.FromArgb(18, 89, 159), 2.6f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var fillBrush = new SolidBrush(Color.FromArgb(80, 41, 118, 190));
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(glowPen, path);
        graphics.DrawPath(outlinePen, path);
    }

    private void DrawTooltip(Graphics graphics, string text)
    {
        using var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font);
        var x = Math.Min(Width - size.Width - 28, _hoverLocation.X + 14);
        var y = Math.Min(Height - size.Height - 20, _hoverLocation.Y + 14);
        x = Math.Max(8, x);
        y = Math.Max(8, y);
        var rect = new Rectangle(
            x,
            y,
            size.Width + 18,
            size.Height + 10);
        using var brush = new SolidBrush(Color.FromArgb(250, 252, 255));
        using var pen = new Pen(Color.FromArgb(158, 178, 204));
        graphics.FillRectangle(brush, rect);
        graphics.DrawRectangle(pen, rect);
        TextRenderer.DrawText(graphics, text, font, rect, Color.FromArgb(27, 43, 63), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private MapCountry? HitTest(Point point)
    {
        var mapBounds = GetMapBounds();
        for (var i = _countries.Count - 1; i >= 0; i--)
        {
            var country = _countries[i];
            using var path = BuildPath(country, mapBounds);
            if (path.IsVisible(point))
            {
                return country;
            }
        }

        return null;
    }

    private GraphicsPath BuildPath(MapCountry country, RectangleF mapBounds)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        foreach (var polygon in country.Polygons)
        {
            foreach (var ring in polygon)
            {
                if (ring.Count < 3)
                {
                    continue;
                }

                var points = ring.Select(point => Project(point, mapBounds)).ToArray();
                path.AddPolygon(points);
            }
        }

        return path;
    }

    private static PointF Project(MapPoint point, RectangleF mapBounds)
    {
        var x = mapBounds.Left + (float)((point.Longitude + 180d) / 360d * mapBounds.Width);
        var y = mapBounds.Top + (float)((90d - point.Latitude) / 180d * mapBounds.Height);
        return new PointF(x, y);
    }

    private RectangleF GetMapBounds()
    {
        var padding = 6;
        var availableWidth = Math.Max(10, Width - padding * 2);
        var availableHeight = Math.Max(10, Height - padding * 2);
        var targetHeight = availableWidth / 2f;
        RectangleF baseBounds;
        if (targetHeight > availableHeight)
        {
            var targetWidth = availableHeight * 2f;
            baseBounds = new RectangleF((Width - targetWidth) / 2f, padding, targetWidth, availableHeight);
        }
        else
        {
            baseBounds = new RectangleF(padding, (Height - targetHeight) / 2f, availableWidth, targetHeight);
        }

        var zoomedWidth = baseBounds.Width * _zoom;
        var zoomedHeight = baseBounds.Height * _zoom;
        return new RectangleF(
            baseBounds.Left + (baseBounds.Width - zoomedWidth) / 2f + _panOffset.X,
            baseBounds.Top + (baseBounds.Height - zoomedHeight) / 2f + _panOffset.Y,
            zoomedWidth,
            zoomedHeight);
    }

    private bool IsAvailable(string countryName)
    {
        return _availableCountryNames.Contains(countryName);
    }

    private void LoadCountries()
    {
        var path = AppResources.MapPath;
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "Assets", "ne_110m_admin_0_countries.geojson");
        }

        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "ne_110m_admin_0_countries.geojson");
        }

        if (!File.Exists(path))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            var chineseName = GetString(properties, "NAME_ZH");
            if (string.IsNullOrWhiteSpace(chineseName))
            {
                continue;
            }

            var geometry = feature.GetProperty("geometry");
            var type = GetString(geometry, "type");
            var coordinates = geometry.GetProperty("coordinates");
            var polygons = type switch
            {
                "Polygon" => [ReadPolygon(coordinates)],
                "MultiPolygon" => coordinates.EnumerateArray().Select(ReadPolygon).ToList(),
                _ => [],
            };

            if (polygons.Count > 0)
            {
                _countries.Add(new MapCountry(chineseName, polygons));
            }
        }
    }

    private static List<List<MapPoint>> ReadPolygon(JsonElement polygonElement)
    {
        var polygon = new List<List<MapPoint>>();
        foreach (var ringElement in polygonElement.EnumerateArray())
        {
            var ring = new List<MapPoint>();
            foreach (var pointElement in ringElement.EnumerateArray())
            {
                var longitude = pointElement[0].GetDouble();
                var latitude = pointElement[1].GetDouble();
                ring.Add(new MapPoint(longitude, latitude));
            }

            polygon.Add(ring);
        }

        return polygon;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record MapCountry(string ChineseName, List<List<List<MapPoint>>> Polygons);

    private readonly record struct MapPoint(double Longitude, double Latitude);
}
