// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SkiaRenderContext.cs" company="OxyPlot">
//   Copyright (c) 2020 OxyPlot contributors
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
//modified copy of https://github.com/oxyplot/oxyplot/blob/develop/Source/OxyPlot.SkiaSharp/SkiaRenderContext.cs
// see https://github.com/oxyplot/oxyplot/pull/1857

using System.Reflection;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace OxyPlot.SkiaSharp;
/// <summary>
/// Implements <see cref="IRenderContext" /> based on SkiaSharp.
/// </summary>
public class SkiaRenderContext : IRenderContext, IDisposable
{
    private readonly Dictionary<FontDescriptor, SKShaper> shaperCache = new();
    private readonly Dictionary<FontDescriptor, SKTypeface> typefaceCache = new();
    private SKPaint paint = new();
    private SKPath path = new();

    private readonly Dictionary<int, string> FontWeightToString = new()
    {
        [100] = "Thin",
        [200] = "ExtraLight",
        [300] = "Light",
        [400] = "Regular",
        [500] = "Medium",
        [600] = "SemiBold",
        [700] = "Bold",
        [800] = "ExtraBold",
        [900] = "Black"
    };

    /// <summary>
    /// Gets or sets the DPI scaling factor. A value of 1 corresponds to 96 DPI (dots per inch).
    /// </summary>
    public float DpiScale { get; set; } = 1;

    /// <inheritdoc />
    public bool RendersToScreen => RenderTarget == RenderTarget.Screen;

    /// <summary>
    /// Gets or sets the render target.
    /// </summary>
    public RenderTarget RenderTarget { get; set; } = RenderTarget.Screen;

    /// <summary>
    /// Gets or sets the <see cref="SKCanvas"/> the <see cref="SkiaRenderContext"/> renders to. This must be set before any draw calls.
    /// </summary>
    public SKCanvas SkCanvas { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether text shaping should be used when rendering text.
    /// </summary>
    public bool UseTextShaping { get; set; } = true;

    /// <summary>
    /// Gets or sets the Miter limit. This is the maximum ratio between Miter length and stroke thickness. When this ration is exceeded, the join falls back to a Bevel. The default value is 10.
    /// </summary>
    public float MiterLimit { get; set; } = 10;

    /// <summary>
    /// Gets a value indicating whether the context renders to pixels.
    /// </summary>
    /// <value><c>true</c> if the context renders to pixels; otherwise, <c>false</c>.</value>
    private bool RendersToPixels => RenderTarget != RenderTarget.VectorGraphic;

    /// <inheritdoc/>
    public int ClipCount => SkCanvas?.SaveCount - 1 ?? 0;

    /// <inheritdoc/>
    public void CleanUp()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public void DrawEllipse(OxyRect extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode)
    {
        if (!fill.IsVisible() && !(stroke.IsVisible() || thickness <= 0))
        {
            return;
        }

        var actualRect = Convert(extents);

        if (fill.IsVisible())
        {
            var paint = GetFillPaint(fill, edgeRenderingMode);
            SkCanvas.DrawOval(actualRect, paint);
        }

        if (stroke.IsVisible() && thickness > 0)
        {
            var paint = GetStrokePaint(stroke, thickness, edgeRenderingMode);
            SkCanvas.DrawOval(actualRect, paint);
        }
    }

    /// <inheritdoc/>
    public void DrawEllipses(IList<OxyRect> extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode)
    {
        if (!fill.IsVisible() && (!stroke.IsVisible() || thickness <= 0))
        {
            return;
        }

        var path = GetPath();
        foreach (var extent in extents)
        {
            path.AddOval(Convert(extent));
        }

        if (fill.IsVisible())
        {
            var paint = GetFillPaint(fill, edgeRenderingMode);
            SkCanvas.DrawPath(path, paint);
        }

        if (stroke.IsVisible() && thickness > 0)
        {
            var paint = GetStrokePaint(stroke, thickness, edgeRenderingMode);
            SkCanvas.DrawPath(path, paint);
        }
    }

    /// <inheritdoc/>
    public void DrawImage(
        OxyImage source,
        double srcX,
        double srcY,
        double srcWidth,
        double srcHeight,
        double destX,
        double destY,
        double destWidth,
        double destHeight,
        double opacity,
        bool interpolate)
    {
        if (source == null)
        {
            return;
        }

        var bytes = source.GetData();
        var image = SKBitmap.Decode(bytes);

        var src = new SKRect((float)srcX, (float)srcY, (float)(srcX + srcWidth), (float)(srcY + srcHeight));
        var dest = new SKRect(Convert(destX), Convert(destY), Convert(destX + destWidth), Convert(destY + destHeight));

        var paint = GetImagePaint(opacity, interpolate);
        SkCanvas.DrawBitmap(image, src, dest, paint);
    }

    /// <inheritdoc/>
    public void DrawLine(
        IList<ScreenPoint> points,
        OxyColor stroke,
        double thickness,
        EdgeRenderingMode edgeRenderingMode,
        double[] dashArray = null,
        LineJoin lineJoin = LineJoin.Miter)
    {
        if (points.Count < 2 || !stroke.IsVisible() || thickness <= 0)
        {
            return;
        }

        var path = GetPath();
        var paint = GetLinePaint(stroke, thickness, edgeRenderingMode, dashArray, lineJoin);
        var actualPoints = GetActualPoints(points, thickness, edgeRenderingMode);
        AddPoints(actualPoints, path);

        SkCanvas.DrawPath(path, paint);
    }

    /// <inheritdoc/>
    public void DrawLineSegments(
        IList<ScreenPoint> points,
        OxyColor stroke,
        double thickness,
        EdgeRenderingMode edgeRenderingMode,
        double[] dashArray = null,
        LineJoin lineJoin = LineJoin.Miter)
    {
        if (points.Count < 2 || !stroke.IsVisible() || thickness <= 0)
        {
            return;
        }

        var paint = GetLinePaint(stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

        var skPoints = new SKPoint[points.Count];
        switch (edgeRenderingMode)
        {
            case EdgeRenderingMode.Automatic when RendersToPixels:
            case EdgeRenderingMode.Adaptive when RendersToPixels:
            case EdgeRenderingMode.PreferSharpness when RendersToPixels:
                var snapOffset = GetSnapOffset(thickness, edgeRenderingMode);
                for (var i = 0; i < points.Count - 1; i += 2)
                {
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    if (RenderContextBase.IsStraightLine(p1, p2))
                    {
                        skPoints[i] = ConvertSnap(p1, snapOffset);
                        skPoints[i + 1] = ConvertSnap(p2, snapOffset);
                    }
                    else
                    {
                        skPoints[i] = Convert(p1);
                        skPoints[i + 1] = Convert(p2);
                    }
                }

                break;
            default:
                for (var i = 0; i < points.Count; i += 2)
                {
                    skPoints[i] = Convert(points[i]);
                    skPoints[i + 1] = Convert(points[i + 1]);
                }

                break;
        }

        SkCanvas.DrawPoints(SKPointMode.Lines, skPoints, paint);
    }

    /// <inheritdoc/>
    public void DrawPolygon(
        IList<ScreenPoint> points,
        OxyColor fill,
        OxyColor stroke,
        double thickness,
        EdgeRenderingMode edgeRenderingMode,
        double[] dashArray = null,
        LineJoin lineJoin = LineJoin.Miter)
    {
        if (!fill.IsVisible() && !(stroke.IsVisible() || thickness <= 0) || points.Count < 2)
        {
            return;
        }

        var path = GetPath();
        var actualPoints = GetActualPoints(points, thickness, edgeRenderingMode);
        AddPoints(actualPoints, path);
        path.Close();

        if (fill.IsVisible())
        {
            var paint = GetFillPaint(fill, edgeRenderingMode);
            SkCanvas.DrawPath(path, paint);
        }

        if (stroke.IsVisible() && thickness > 0)
        {
            var paint = GetLinePaint(stroke, thickness, edgeRenderingMode, dashArray, lineJoin);
            SkCanvas.DrawPath(path, paint);
        }
    }

    /// <inheritdoc/>
    public void DrawPolygons(
        IList<IList<ScreenPoint>> polygons,
        OxyColor fill,
        OxyColor stroke,
        double thickness,
        EdgeRenderingMode edgeRenderingMode,
        double[] dashArray = null,
        LineJoin lineJoin = LineJoin.Miter)
    {
        if (!fill.IsVisible() && !(stroke.IsVisible() || thickness <= 0) || polygons.Count == 0)
        {
            return;
        }

        var path = GetPath();
        foreach (var polygon in polygons)
        {
            if (polygon.Count < 2)
            {
                continue;
            }

            var actualPoints = GetActualPoints(polygon, thickness, edgeRenderingMode);
            AddPoints(actualPoints, path);
            path.Close();
        }

        if (fill.IsVisible())
        {
            var paint = GetFillPaint(fill, edgeRenderingMode);
            SkCanvas.DrawPath(path, paint);
        }

        if (stroke.IsVisible() && thickness > 0)
        {
            var paint = GetLinePaint(stroke, thickness, edgeRenderingMode, dashArray, lineJoin);
            SkCanvas.DrawPath(path, paint);
        }
    }

    /// <inheritdoc/>
    public void DrawRectangle(OxyRect rectangle, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode)
    {
        if (!fill.IsVisible() && !(stroke.IsVisible() || thickness <= 0))
        {
            return;
        }

        var actualRectangle = GetActualRect(rectangle, thickness, edgeRenderingMode);

        if (fill.IsVisible())
        {
            var paint = GetFillPaint(fill, edgeRenderingMode);
            SkCanvas.DrawRect(actualRectangle, paint);
        }

        if (stroke.IsVisible() && thickness > 0)
        {
            var paint = GetStrokePaint(stroke, thickness, edgeRenderingMode);
            SkCanvas.DrawRect(actualRectangle, paint);
        }
    }

    /// <inheritdoc/>
    public void DrawRectangles(IList<OxyRect> rectangles, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode)
    {
        if (!fill.IsVisible() && !(stroke.IsVisible() || thickness <= 0) || rectangles.Count == 0)
        {
            return;
        }

        var path = GetPath();
        foreach (var rectangle in GetActualRects(rectangles, thickness, edgeRenderingMode))
        {
            path.AddRect(rectangle);
        }

        if (fill.IsVisible())
        {
            var paint = GetFillPaint(fill, edgeRenderingMode);
            SkCanvas.DrawPath(path, paint);
        }

        if (stroke.IsVisible() && thickness > 0)
        {
            var paint = GetStrokePaint(stroke, thickness, edgeRenderingMode);
            SkCanvas.DrawPath(path, paint);
        }
    }

    /// <inheritdoc/>
    public void DrawText(
        ScreenPoint p,
        string text,
        OxyColor fill,
        string fontFamily = null,
        double fontSize = 10,
        double fontWeight = 400,
        double rotation = 0,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top,
        OxySize? maxSize = null)
    {
        if (text == null || !fill.IsVisible())
        {
            return;
        }

        var paint = GetTextPaint(fontFamily, fontSize, fontWeight, out var shaper);
        paint.Color = fill.ToSKColor();

        var x = Convert(p.X);
        var y = Convert(p.Y);

        var lines = StringHelper.SplitLines(text);
        var lineHeight = paint.GetFontMetrics(out var metrics);

        var deltaY = verticalAlignment switch
        {
            VerticalAlignment.Top => -metrics.Ascent,
            VerticalAlignment.Middle => -(metrics.Ascent + metrics.Descent + lineHeight * (lines.Length - 1)) / 2,
            VerticalAlignment.Bottom => -metrics.Descent - lineHeight * (lines.Length - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(verticalAlignment))
        };

        using var _ = new SKAutoCanvasRestore(SkCanvas);
        SkCanvas.Translate(x, y);
        SkCanvas.RotateDegrees((float)rotation);

        foreach (var line in lines)
        {
            if (UseTextShaping)
            {
                var width = MeasureText(line, shaper, paint);
                var deltaX = horizontalAlignment switch
                {
                    HorizontalAlignment.Left => 0,
                    HorizontalAlignment.Center => -width / 2,
                    HorizontalAlignment.Right => -width,
                    _ => throw new ArgumentOutOfRangeException(nameof(horizontalAlignment))
                };

                this.paint.TextAlign = SKTextAlign.Left;
                SkCanvas.DrawShapedText(shaper, line, deltaX, deltaY, paint);
            }
            else
            {
                paint.TextAlign = horizontalAlignment switch
                {
                    HorizontalAlignment.Left => SKTextAlign.Left,
                    HorizontalAlignment.Center => SKTextAlign.Center,
                    HorizontalAlignment.Right => SKTextAlign.Right,
                    _ => throw new ArgumentOutOfRangeException(nameof(horizontalAlignment))
                };

                SkCanvas.DrawText(line, 0, deltaY, paint);
            }

            deltaY += lineHeight;
        }
    }

    /// <inheritdoc/>
    public OxySize MeasureText(string text, string fontFamily = null, double fontSize = 10, double fontWeight = 500)
    {
        if (text == null)
        {
            return new OxySize(0, 0);
        }

        var lines = StringHelper.SplitLines(text);
        var paint = GetTextPaint(fontFamily, fontSize, fontWeight, out var shaper);
        var height = paint.GetFontMetrics(out _) * lines.Length;
        var width = lines.Max(line => MeasureText(line, shaper, paint));

        return new OxySize(ConvertBack(width), ConvertBack(height));
    }

    /// <inheritdoc/>
    public void PopClip()
    {
        if (SkCanvas.SaveCount == 1)
        {
            throw new InvalidOperationException("Unbalanced call to PopClip.");
        }

        SkCanvas.Restore();
    }

    /// <inheritdoc/>
    public void PushClip(OxyRect clippingRectangle)
    {
        SkCanvas.Save();
        SkCanvas.ClipRect(Convert(clippingRectangle));
    }

    /// <inheritdoc/>
    public void SetToolTip(string text)
    {
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">A value indicating whether this method is called from the Dispose method.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        paint?.Dispose();
        paint = null;
        path?.Dispose();
        path = null;

        foreach (var typeface in typefaceCache.Values)
        {
            typeface.Dispose();
        }

        typefaceCache.Clear();

        foreach (var shaper in shaperCache.Values)
        {
            shaper.Dispose();
        }

        shaperCache.Clear();
    }

    /// <summary>
    /// Adds the <see cref="SKPoint"/>s to the <see cref="SKPath"/> as a series of connected lines.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="path">The path.</param>
    private static void AddPoints(IEnumerable<SKPoint> points, SKPath path)
    {
        using var e = points.GetEnumerator();
        if (!e.MoveNext())
        {
            return;
        }

        path.MoveTo(e.Current);
        while (e.MoveNext())
        {
            path.LineTo(e.Current);
        }
    }

    /// <summary>
    /// Gets the pixel offset that a line with the specified thickness should snap to.
    /// </summary>
    /// <remarks>
    /// This takes into account that lines with even stroke thickness should be snapped to the border between two pixels while lines with odd stroke thickness should be snapped to the middle of a pixel.
    /// </remarks>
    /// <param name="thickness">The line thickness.</param>
    /// <returns>The snap offset.</returns>
    private static float GetSnapOffset(float thickness)
    {
        var mod = thickness % 2;
        var isOdd = mod >= 0.5 && mod < 1.5;
        return isOdd ? 0.5f : 0;
    }

    /// <summary>
    /// Snaps a value to a pixel with the specified offset.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The snapped value.</returns>
    private static float Snap(float value, float offset)
    {
        return (float)Math.Round(value + offset, MidpointRounding.AwayFromZero) - offset;
    }

    /// <summary>
    /// Converts a <see cref="OxyRect"/> to a <see cref="SKRect"/>, taking into account DPI scaling.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The converted rectangle.</returns>
    private SKRect Convert(OxyRect rect)
    {
        var left = Convert(rect.Left);
        var right = Convert(rect.Right);
        var top = Convert(rect.Top);
        var bottom = Convert(rect.Bottom);
        return new SKRect(left, top, right, bottom);
    }

    /// <summary>
    /// Converts a <see cref="double"/> to a <see cref="float"/>, taking into account DPI scaling.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The converted value.</returns>
    private float Convert(double value)
    {
        return (float)value * DpiScale;
    }

    /// <summary>
    /// Converts <see cref="ScreenPoint"/> to a <see cref="SKPoint"/>, taking into account DPI scaling.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The converted point.</returns>
    private SKPoint Convert(ScreenPoint point)
    {
        return new SKPoint(Convert(point.X), Convert(point.Y));
    }

    /// <summary>
    /// Converts a <see cref="float"/> to a <see cref="double"/>, applying reversed DPI scaling.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The converted value.</returns>
    private double ConvertBack(float value)
    {
        return value / DpiScale;
    }

    /// <summary>
    /// Converts <see cref="double"/> dash array to a <see cref="float"/> array, taking into account DPI scaling.
    /// </summary>
    /// <param name="values">The array of values.</param>
    /// <param name="strokeThickness">The stroke thickness.</param>
    /// <returns>The array of converted values.</returns>
    private float[] ConvertDashArray(double[] values, float strokeThickness)
    {
        var ret = new float[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            ret[i] = Convert(values[i]) * strokeThickness;
        }

        return ret;
    }

    /// <summary>
    /// Converts a <see cref="OxyRect"/> to a <see cref="SKRect"/>, taking into account DPI scaling and snapping the corners to pixels.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="snapOffset">The snapping offset.</param>
    /// <returns>The converted rectangle.</returns>
    private SKRect ConvertSnap(OxyRect rect, float snapOffset)
    {
        var left = ConvertSnap(rect.Left, snapOffset);
        var right = ConvertSnap(rect.Right, snapOffset);
        var top = ConvertSnap(rect.Top, snapOffset);
        var bottom = ConvertSnap(rect.Bottom, snapOffset);
        return new SKRect(left, top, right, bottom);
    }

    /// <summary>
    /// Converts a <see cref="double"/> to a <see cref="float"/>, taking into account DPI scaling and snapping the value to a pixel.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="snapOffset">The snapping offset.</param>
    /// <returns>The converted value.</returns>
    private float ConvertSnap(double value, float snapOffset)
    {
        return Snap(Convert(value), snapOffset);
    }

    /// <summary>
    /// Converts <see cref="ScreenPoint"/> to a <see cref="SKPoint"/>, taking into account DPI scaling and snapping the point to a pixel.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="snapOffset">The snapping offset.</param>
    /// <returns>The converted point.</returns>
    private SKPoint ConvertSnap(ScreenPoint point, float snapOffset)
    {
        return new SKPoint(ConvertSnap(point.X, snapOffset), ConvertSnap(point.Y, snapOffset));
    }

    /// <summary>
    /// Gets the <see cref="SKPoint"/>s that should actually be rendered from the list of <see cref="ScreenPoint"/>s, taking into account DPI scaling and snapping if necessary.
    /// </summary>
    /// <param name="screenPoints">The points.</param>
    /// <param name="strokeThickness">The stroke thickness.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns>The actual points.</returns>
    private IEnumerable<SKPoint> GetActualPoints(IList<ScreenPoint> screenPoints, double strokeThickness, EdgeRenderingMode edgeRenderingMode)
    {
        switch (edgeRenderingMode)
        {
            case EdgeRenderingMode.Automatic when RendersToPixels && RenderContextBase.IsStraightLine(screenPoints):
            case EdgeRenderingMode.Adaptive when RendersToPixels && RenderContextBase.IsStraightLine(screenPoints):
            case EdgeRenderingMode.PreferSharpness when RendersToPixels:
                var snapOffset = GetSnapOffset(strokeThickness, edgeRenderingMode);
                return screenPoints.Select(p => ConvertSnap(p, snapOffset));
            default:
                return screenPoints.Select(Convert);
        }
    }

    /// <summary>
    /// Gets the <see cref="SKRect"/> that should actually be rendered from the <see cref="OxyRect"/>, taking into account DPI scaling and snapping if necessary.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="strokeThickness">The stroke thickness.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns>The actual rectangle.</returns>
    private SKRect GetActualRect(OxyRect rect, double strokeThickness, EdgeRenderingMode edgeRenderingMode)
    {
        switch (edgeRenderingMode)
        {
            case EdgeRenderingMode.Adaptive when RendersToPixels:
            case EdgeRenderingMode.Automatic when RendersToPixels:
            case EdgeRenderingMode.PreferSharpness when RendersToPixels:
                var actualThickness = GetActualThickness(strokeThickness, edgeRenderingMode);
                var snapOffset = GetSnapOffset(actualThickness);
                return ConvertSnap(rect, snapOffset);
            default:
                return Convert(rect);
        }
    }

    /// <summary>
    /// Gets the <see cref="SKRect"/>s that should actually be rendered from the list of <see cref="OxyRect"/>s, taking into account DPI scaling and snapping if necessary.
    /// </summary>
    /// <param name="rects">The rectangles.</param>
    /// <param name="strokeThickness">The stroke thickness.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns>The actual rectangles.</returns>
    private IEnumerable<SKRect> GetActualRects(IEnumerable<OxyRect> rects, double strokeThickness, EdgeRenderingMode edgeRenderingMode)
    {
        switch (edgeRenderingMode)
        {
            case EdgeRenderingMode.Adaptive when RendersToPixels:
            case EdgeRenderingMode.Automatic when RendersToPixels:
            case EdgeRenderingMode.PreferSharpness when RendersToPixels:
                var actualThickness = GetActualThickness(strokeThickness, edgeRenderingMode);
                var snapOffset = GetSnapOffset(actualThickness);
                return rects.Select(rect => ConvertSnap(rect, snapOffset));
            default:
                return rects.Select(Convert);
        }
    }

    /// <summary>
    /// Gets the stroke thickness that should actually be used for rendering, taking into account DPI scaling and snapping if necessary.
    /// </summary>
    /// <param name="strokeThickness">The stroke thickness.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns>The actual stroke thickness.</returns>
    private float GetActualThickness(double strokeThickness, EdgeRenderingMode edgeRenderingMode)
    {
        var scaledThickness = Convert(strokeThickness);
        if (edgeRenderingMode == EdgeRenderingMode.PreferSharpness && RendersToPixels)
        {
            scaledThickness = Snap(scaledThickness, 0);
        }

        return scaledThickness;
    }

    /// <summary>
    /// Gets a <see cref="SKPaint"/> containing information needed to render the fill of a shape.
    /// </summary>
    /// <remarks>
    /// This modifies and returns the local <see cref="paint"/> instance.
    /// </remarks>
    /// <param name="fillColor">The fill color.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns>The paint.</returns>
    private SKPaint GetFillPaint(OxyColor fillColor, EdgeRenderingMode edgeRenderingMode)
    {
        paint.Color = fillColor.ToSKColor();
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = ShouldUseAntiAliasing(edgeRenderingMode);
        paint.PathEffect = null;
        return paint;
    }

    /// <summary>
    /// Gets a <see cref="SKPaint"/> containing information needed to render an image.
    /// </summary>
    /// <remarks>
    /// This modifies and returns the local <see cref="paint"/> instance.
    /// </remarks>
    /// <param name="opacity">The opacity.</param>
    /// <param name="interpolate">A value indicating whether interpolation should be used.</param>
    /// <returns>The paint.</returns>
    private SKPaint GetImagePaint(double opacity, bool interpolate)
    {
        paint.Color = new SKColor(0, 0, 0, (byte)(255 * opacity));
        paint.FilterQuality = interpolate ? SKFilterQuality.High : SKFilterQuality.None;
        paint.IsAntialias = true;
        return paint;
    }

    /// <summary>
    /// Gets a <see cref="SKPaint"/> containing information needed to render a line.
    /// </summary>
    /// <remarks>
    /// This modifies and returns the local <see cref="paint"/> instance.
    /// </remarks>
    /// <param name="strokeColor">The stroke color.</param>
    /// <param name="strokeThickness">The stroke thickness.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <param name="dashArray">The dash array.</param>
    /// <param name="lineJoin">The line join.</param>
    /// <returns>The paint.</returns>
    private SKPaint GetLinePaint(OxyColor strokeColor, double strokeThickness, EdgeRenderingMode edgeRenderingMode, double[] dashArray, LineJoin lineJoin)
    {
        var paint = GetStrokePaint(strokeColor, strokeThickness, edgeRenderingMode);

        if (dashArray != null)
        {
            var actualDashArray = ConvertDashArray(dashArray, paint.StrokeWidth);
            paint.PathEffect = SKPathEffect.CreateDash(actualDashArray, 0);
        }

        paint.StrokeJoin = lineJoin switch
        {
            LineJoin.Miter => SKStrokeJoin.Miter,
            LineJoin.Round => SKStrokeJoin.Round,
            LineJoin.Bevel => SKStrokeJoin.Bevel,
            _ => throw new ArgumentOutOfRangeException(nameof(lineJoin))
        };

        return paint;
    }

    /// <summary>
    /// Gets an empty <see cref="SKPath"/>.
    /// </summary>
    /// <remarks>
    /// This clears and returns the local <see cref="path"/> instance.
    /// </remarks>
    /// <returns>The path.</returns>
    private SKPath GetPath()
    {
        path.Reset();
        return path;
    }

    /// <summary>
    /// Gets the snapping offset for the specified stroke thickness.
    /// </summary>
    /// <remarks>
    /// This takes into account that lines with even stroke thickness should be snapped to the border between two pixels while lines with odd stroke thickness should be snapped to the middle of a pixel.
    /// </remarks>
    /// <param name="thickness">The stroke thickness.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns>The snap offset.</returns>
    private float GetSnapOffset(double thickness, EdgeRenderingMode edgeRenderingMode)
    {
        var actualThickness = GetActualThickness(thickness, edgeRenderingMode);
        return GetSnapOffset(actualThickness);
    }

    /// <summary>
    /// Gets a <see cref="SKPaint"/> containing information needed to render a stroke.
    /// </summary>
    /// <remarks>
    /// This modifies and returns the local <see cref="paint"/> instance.
    /// </remarks>
    /// <param name="strokeColor">The stroke color.</param>
    /// <param name="strokeThickness">The stroke thickness.</param>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns>The paint.</returns>
    private SKPaint GetStrokePaint(OxyColor strokeColor, double strokeThickness, EdgeRenderingMode edgeRenderingMode)
    {
        paint.Color = strokeColor.ToSKColor();
        paint.Style = SKPaintStyle.Stroke;
        paint.IsAntialias = ShouldUseAntiAliasing(edgeRenderingMode);
        paint.StrokeWidth = GetActualThickness(strokeThickness, edgeRenderingMode);
        paint.PathEffect = null;
        paint.StrokeJoin = SKStrokeJoin.Miter;
        paint.StrokeMiter = MiterLimit;
        return paint;
    }

    /// <summary>
    /// Gets a <see cref="SKPaint"/> containing information needed to render text.
    /// </summary>
    /// <remarks>
    /// This modifies and returns the local <see cref="paint"/> instance.
    /// </remarks>
    /// <param name="fontFamily">The font family.</param>
    /// <param name="fontSize">The font size.</param>
    /// <param name="fontWeight">The font weight.</param>
    /// <param name="shaper">The font shaper.</param>
    /// <returns>The paint.</returns>
    private SKPaint GetTextPaint(string fontFamily, double fontSize, double fontWeight, out SKShaper shaper)
    {
        var fontDescriptor = new FontDescriptor(fontFamily, fontWeight);
        if (!typefaceCache.TryGetValue(fontDescriptor, out var typeface))
        {
            typeface = SKTypeface.FromFamilyName(fontFamily, new SKFontStyle((int)fontWeight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));
            if (typeface.FamilyName != fontFamily) // requested font not found or WASM
            {
                try
                {
                    var assembly = Assembly.GetEntryAssembly(); // the executing programs
                    var weight = (FontWeightToString.ContainsKey((int)fontWeight) ? FontWeightToString[(int)fontWeight]: "Regular");
                    var filename = $"{fontFamily}-{weight}.ttf".ToLower();
                    Console.WriteLine($"Load Font {filename}");

                    var matches = assembly!.GetManifestResourceNames().Where(item => item.ToLower().EndsWith(filename));
                    if (!matches.Any()) matches = assembly!.GetManifestResourceNames().Where(item => item.ToLower().EndsWith(fontFamily + ".ttf"));
                    foreach (var item in matches)
                    {
                        var s = assembly.GetManifestResourceStream(item);
                        typeface = SKTypeface.FromStream(s);
                    }
                }
                catch {
                    Console.WriteLine($"Requested Font {fontFamily} could not be found, falling back to {typeface.FamilyName}");
                }
            }
            typefaceCache.Add(fontDescriptor, typeface);
        }

        if (UseTextShaping)
        {
            if (!shaperCache.TryGetValue(fontDescriptor, out shaper))
            {
                shaper = new SKShaper(typeface);
                shaperCache.Add(fontDescriptor, shaper);
            }
        }
        else
        {
            shaper = null;
        }

        paint.Typeface = typeface;
        paint.TextSize = Convert(fontSize);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;
        paint.HintingLevel = RendersToScreen ? SKPaintHinting.Full : SKPaintHinting.NoHinting;
        paint.SubpixelText = RendersToScreen;
        return paint;
    }

    /// <summary>
    /// Measures text using the specified <see cref="SKShaper"/> and <see cref="SKPaint"/>.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="shaper">The text shaper.</param>
    /// <param name="paint">The paint.</param>
    /// <returns>The width of the text when rendered using the specified shaper and paint.</returns>
    private float MeasureText(string text, SKShaper shaper, SKPaint paint)
    {
        if (!UseTextShaping)
        {
            return paint.MeasureText(text);
        }

        // we have to get a bit creative here as SKShaper does not offer a direct overload for this.
        // see also https://github.com/mono/SkiaSharp/blob/master/source/SkiaSharp.HarfBuzz/SkiaSharp.HarfBuzz.Shared/SKShaper.cs
        using var buffer = new HarfBuzzSharp.Buffer();
        switch (paint.TextEncoding)
        {
            case SKTextEncoding.Utf8:
                buffer.AddUtf8(text);
                break;
            case SKTextEncoding.Utf16:
                buffer.AddUtf16(text);
                break;
            case SKTextEncoding.Utf32:
                buffer.AddUtf32(text);
                break;
            default:
                throw new NotSupportedException("TextEncoding is not supported.");
        }

        buffer.GuessSegmentProperties();
        shaper.Shape(buffer, paint);
        return buffer.GlyphPositions.Sum(gp => gp.XAdvance) * paint.TextSize / 512;
    }

    /// <summary>
    /// Gets a value indicating whether anti-aliasing should be used taking in account the specified edge rendering mode.
    /// </summary>
    /// <param name="edgeRenderingMode">The edge rendering mode.</param>
    /// <returns><c>true</c> if anti-aliasing should be used; <c>false</c> otherwise.</returns>
    private bool ShouldUseAntiAliasing(EdgeRenderingMode edgeRenderingMode)
    {
        return edgeRenderingMode != EdgeRenderingMode.PreferSpeed;
    }

    /// <summary>
    /// Represents a font description.
    /// </summary>
    private struct FontDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FontDescriptor"/> struct.
        /// </summary>
        /// <param name="fontFamily">The font family.</param>
        /// <param name="fontWeight">The font weight.</param>
        public FontDescriptor(string fontFamily, double fontWeight)
        {
            FontFamily = fontFamily;
            FontWeight = fontWeight;
        }

        /// <summary>
        /// The font family.
        /// </summary>
        public string FontFamily { get; }

        /// <summary>
        /// The font weight.
        /// </summary>
        public double FontWeight { get; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is FontDescriptor other && FontFamily == other.FontFamily && FontWeight == other.FontWeight;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hashCode = -1030903623;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FontFamily);
            hashCode = hashCode * -1521134295 + FontWeight.GetHashCode();
            return hashCode;
        }
    }
}
