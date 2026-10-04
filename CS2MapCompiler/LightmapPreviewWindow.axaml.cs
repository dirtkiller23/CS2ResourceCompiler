using System.Globalization;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CS2MapCompiler.LightmapPreview;

namespace CS2MapCompiler;

[SupportedOSPlatform("windows")]
public partial class LightmapPreviewWindow : Window
{
    private LightmapAtlas? atlas;

    public LightmapPreviewWindow()
    {
        InitializeComponent();

        ExposureSlider.ValueChanged += (_, e) => SetExposure((float)e.NewValue);
    }

    public void ShowUpdate(LightmapPreviewUpdate update)
    {
        if (update.IsNewAtlas || atlas != update.Atlas)
        {
            atlas = update.Atlas;
            atlas.SetExposure((float)ExposureSlider.Value);
            View.SetAtlas(atlas);
            SaveButton.IsEnabled = true;
        }
        else
        {
            View.Refresh(update.Pixels);
        }

        var count = update.Atlas.BlockCount;
        var size = $"{update.Atlas.Width}x{update.Atlas.Height}";

        Status.Text = update.Stage switch
        {
            LightmapPreviewStage.Waiting => $"Waiting for vrad3 to start baking {count} blocks  |  {size}",
            LightmapPreviewStage.Baking => $"Baking block {update.BlockIndex + 1} of {count}  |  {size}  |  before filtering",
            LightmapPreviewStage.BlockDone => $"Block {update.BlockIndex + 1} of {count} done  |  {size}  |  before filtering",
            _ => $"Baked and filtered  |  {size}  |  before compression",
        };
    }

    public void ShowEnded(string message)
    {
        Status.Text = message;
    }

    private void SetExposure(float exposure)
    {
        ExposureLabel.Text = exposure.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

        if (atlas == null)
        {
            return;
        }

        atlas.SetExposure(exposure);
        View.Refresh(new PixelRegion(0, 0, atlas.Width, atlas.Height));
    }

    private void OnFit(object? sender, RoutedEventArgs e)
    {
        View.FitToView();
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (View.Bitmap is not { } bitmap)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save lightmap",
            SuggestedFileName = "lightmap.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });

        if (file == null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
