using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CS2MapCompiler.LightmapPreview;

namespace CS2MapCompiler;

// Drag to pan, scroll to zoom, double click to fit it back in view
[SupportedOSPlatform("windows")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "the bitmap is disposed when the view leaves its window")]
public sealed class LightmapView : Control
{
    public static readonly StyledProperty<bool> ShowBlocksProperty = AvaloniaProperty.Register<LightmapView, bool>(nameof(ShowBlocks), true);

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(16, 16, 16));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Colors.Black, 0.35);
    private static readonly IPen BlockPen = new Pen(new SolidColorBrush(Colors.White, 0.3), 1);
    private static readonly IPen BakingPen = new Pen(Brushes.Gold, 2);

    private LightmapAtlas? atlas;
    private WriteableBitmap? bitmap;
    private double zoom = 1;
    private Point offset;
    private Point? dragStart;
    private Point dragOffset;
    private bool fitPending;

    // Keeps the atlas fitted while the view resizes, until it's dragged or zoomed
    private bool autoFit = true;

    static LightmapView()
    {
        AffectsRender<LightmapView>(ShowBlocksProperty);
    }

    public LightmapView()
    {
        ClipToBounds = true;
    }

    public bool ShowBlocks
    {
        get => GetValue(ShowBlocksProperty);
        set => SetValue(ShowBlocksProperty, value);
    }

    public WriteableBitmap? Bitmap => bitmap;

    public void SetAtlas(LightmapAtlas value)
    {
        atlas = value;
        bitmap?.Dispose();
        bitmap = new WriteableBitmap(new PixelSize(value.Width, value.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        autoFit = true;
        fitPending = true;
        Refresh(new PixelRegion(0, 0, value.Width, value.Height));
        InvalidateArrange();
    }

    public void Refresh(PixelRegion pixels)
    {
        if (atlas == null || bitmap == null)
        {
            return;
        }

        using (var buffer = bitmap.Lock())
        {
            atlas.Render(pixels, buffer.Address, buffer.RowBytes);
        }

        InvalidateVisual();
    }

    public void FitToView()
    {
        if (atlas == null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        zoom = Math.Min(Bounds.Width / atlas.Width, Bounds.Height / atlas.Height) * 0.95;
        offset = new Point((Bounds.Width - atlas.Width * zoom) / 2, (Bounds.Height - atlas.Height * zoom) / 2);
        autoFit = true;
        fitPending = false;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        bitmap?.Dispose();
        bitmap = null;
        atlas = null;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        if (fitPending || (autoFit && size != Bounds.Size))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(FitToView);
        }

        return size;
    }

    public override void Render(DrawingContext context)
    {
        // Filled so the whole view picks up pointer input
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        if (atlas == null || bitmap == null)
        {
            return;
        }

        var image = new Rect(offset.X, offset.Y, atlas.Width * zoom, atlas.Height * zoom);

        // Keeps texels crisp when zoomed in
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = zoom >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
        {
            context.DrawImage(bitmap, new Rect(0, 0, atlas.Width, atlas.Height), image);
        }

        var block = atlas.BlockSize * zoom;

        for (var by = 0; by < atlas.BlocksY; by++)
        {
            for (var bx = 0; bx < atlas.BlocksX; bx++)
            {
                var rect = new Rect(offset.X + bx * block, offset.Y + by * block, block, block).Intersect(image);
                var state = atlas.GetBlockState(bx, by);

                // The block being baked gets outlined even when the grid is hidden
                if (state == LightmapBlockState.Baking)
                {
                    context.DrawRectangle(null, BakingPen, rect);
                }
                else if (ShowBlocks)
                {
                    if (state == LightmapBlockState.Pending)
                    {
                        context.FillRectangle(PendingBrush, rect);
                    }

                    context.DrawRectangle(null, BlockPen, rect);
                }
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var at = e.GetPosition(this);
        var newZoom = Math.Clamp(zoom * (e.Delta.Y > 0 ? 1.25 : 0.8), 0.02, 64);
        offset = new Point(at.X - (at.X - offset.X) * newZoom / zoom, at.Y - (at.Y - offset.Y) * newZoom / zoom);
        zoom = newZoom;
        autoFit = false;
        fitPending = false;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.ClickCount == 2)
        {
            FitToView();
            return;
        }

        dragStart = e.GetPosition(this);
        dragOffset = offset;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (dragStart is { } start)
        {
            var at = e.GetPosition(this);
            offset = new Point(dragOffset.X + at.X - start.X, dragOffset.Y + at.Y - start.Y);
            autoFit = false;
            fitPending = false;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        dragStart = null;
        e.Pointer.Capture(null);
        Cursor = null;
    }
}
