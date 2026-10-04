using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Win32;

namespace CS2MapCompiler;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "the compile process is disposed as soon as it exits, and the lightmap preview when the window closes")]
public partial class MainWindow : Window
{
    private string? cs2dir;
    private string? resourcecompiler;
    private string? addonname;
    private string? mapname;
    private string? mappath;
    private string? outputpath;
    private string? arg;
    private bool oldsource2pre2020; // this parameter enables parameters for S2 games pre 2021 and disables the post 2021 parameters.
    private Process? process;

    /// <summary>The compile running, from its start to the end of its output.</summary>
    private Task? compileTask;

    /// <summary>Whether the compile running was cancelled, so its end is not reported as completed.</summary>
    private bool cancelled;

    /// <summary>The compile log, and the lines printed since it was last shown, which arrive from the compiler's threads.</summary>
    private readonly ObservableCollection<LogLine> logLines = [];
    private readonly ConcurrentQueue<LogLine> pendingLines = new();
    private ScrollViewer? logScroller;

    private LightmapPreviewController? lightmapPreview;

    public MainWindow()
    {
        InitializeComponent();

        logList.ItemsSource = logLines;

        // the compiler prints faster than lines can be shown one by one, so what it printed is shown a few times a second
        new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => FlushLog()).Start();

        genLightmaps.IsCheckedChanged += genLightmaps_CheckedChanged;
        entsOnly.IsCheckedChanged += entsOnly_CheckedChanged;

        lightmapres.SelectedIndex = 2;
        lightmapquality.SelectedIndex = 1;

        // any thread count from one to all of them, typed or stepped, and pulled back into that range when it is outside it
        int cpuCount = Environment.ProcessorCount;
        foreach (var box in new[] { threadcount, AudioThreadsBox })
        {
            box.Maximum = cpuCount;
            box.Value = cpuCount;
        }

        Checkers();
        HelpSystemEventReg();
        UpdateArgLabel();

        Loaded += Form1_Load;
        Closed += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
            {
                lightmapPreview?.Dispose();
            }
        };
    }

    private static string? GetCS2Dir()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        string? steamPath = (string?)Registry.GetValue("HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath", "");

        if (string.IsNullOrEmpty(steamPath))
            return null;

        string pathsFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(pathsFile))
            return null;

        List<string> libraries = new List<string>();
        libraries.Add(Path.Combine(steamPath));

        var pathVDF = File.ReadAllLines(pathsFile);
        // Okay, this is not a full vdf-parser, but it seems to work pretty much, since the
        // vdf-grammar is pretty easy. Hopefully it never breaks. I'm too lazy to write a full vdf-parser though.
        Regex pathRegex = new Regex(@"\""(([^\""]*):\\([^\""]*))\""");
        foreach (var line in pathVDF)
        {
            if (pathRegex.IsMatch(line))
            {
                string match = pathRegex.Matches(line)[0].Groups[1].Value;

                // De-Escape vdf.
                libraries.Add(match.Replace("\\\\", "\\", StringComparison.Ordinal));
            }
        }

        foreach (var library in libraries)
        {
            string cs2Path = Path.Combine(library, "steamapps\\common\\Counter-Strike Global Offensive\\game\\bin\\win64");
            if (Directory.Exists(cs2Path))
            {
                return cs2Path;
            }
        }

        return null;
    }

    private async void Form1_Load(object? sender, RoutedEventArgs e)
    {
        cs2dir = GetCS2Dir();
        await CS2Validator();
        gamedir.Text = cs2dir ?? "N/A";
    }

    private async Task CS2Validator()
    {
        string[] requiredExecutables = { "cs2.exe", "hlvr.exe", "project8.exe", "deadlock.exe", "primelock.exe", "hlx.exe", "hl3.exe", "steamtours.exe", "dota2.exe", "deskjob.exe", "vr.exe" };
        bool anyExecutableFound = false;

        foreach (string exe in requiredExecutables)
        {
            if (cs2dir != null && File.Exists(Path.Combine(cs2dir, exe)))
            {
                anyExecutableFound = true;
                SetStatus(cs2statusPill, cs2status, $"Found {exe}", found: true);
                button1.IsEnabled = true;
                if (exe != "cs2.exe" && exe != "project8.exe" && exe != "deadlock.exe" && exe != "hl3.exe" && exe != "hlx.exe" && exe != "dota2.exe" && exe != "primelock.exe")
                { //the future stares back - todo add resourcecompiler parameters for future s2 versions/games
                    oldsource2pre2020 = true;
                }

                if (oldsource2pre2020 == true) //if its not a S2 game post 2021, then assume we are a s2 game pre 2021 and disable post 2021 features like GPU VRAD3.
                                               //todo add support for envmaps and nolight/old light from 2015-2016
                {
                    cpu.IsEnabled = false;
                    cpu.IsVisible = false;
                    legacyCompileColMesh.IsEnabled = false;
                    legacyCompileColMesh.IsVisible = false;
                    bakeCustom.IsEnabled = false;
                    bakeCustom.IsVisible = false;
                    AudioThreadsBox.IsVisible = false;
                    AudioThreadsBox.IsEnabled = false;
                    AudioThreadsLabel.IsVisible = false;
                    AudioThreadsLabel.IsEnabled = false;
                    vrad3LargeSize.IsVisible = false;
                    vrad3LargeSize.IsEnabled = false;
                    cpuLabel.Text = "Only CPU lightmap is supported.";
                }

                if (exe == "deskjob.exe") //if deskjob - disable the lighting options by default as there is no vrad3
                {
                    genLightmaps.IsChecked = false;
                    noiseremoval.IsChecked = false;
                    cpuLabel.Text = "No light is possible.";
                }

                if (exe != "dota2.exe")
                {
                    gridNav.IsEnabled = false;
                    gridNav.IsVisible = false;
                    nolightmaps.IsEnabled = false;
                    nolightmaps.IsVisible = false;
                }
                else
                if (exe == "dota2.exe") //if dota 2 - show grid nav button and uncheck others.
                {
                    gridNav.IsEnabled = true;
                    gridNav.IsVisible = true;
                    genLightmaps.IsChecked = false;
                    noiseremoval.IsChecked = false;
                    buildNav.IsChecked = false;
                    saReverb.IsChecked = false;
                    baPaths.IsChecked = false;
                    bakeCustom.IsChecked = false;
                }

                if (File.Exists(Path.Combine(cs2dir, "resourcecompiler.exe")))
                {
                    SetStatus(wststatusPill, wststatus, "Found", found: true);
                    resourcecompiler = Path.Combine(cs2dir, "resourcecompiler.exe");
                    button1.IsEnabled = true;
                }
                else
                {
                    SetStatus(wststatusPill, wststatus, "Not Found", found: false);
                    button1.IsEnabled = false;
                    await MessageDialog.ShowAsync(this, MessageKind.Warning, "CS2 Map Compiler", "Please Install Workshop Tools!");
                }

                break; // Exit the loop once any executable is found
            }
        }

        if (!anyExecutableFound)
        {
            SetStatus(cs2statusPill, cs2status, "Not Found", found: false);
            SetStatus(wststatusPill, wststatus, "Not Found", found: false);
            button1.IsEnabled = false;
            await MessageDialog.ShowAsync(this, MessageKind.Danger, "CS2 Map Compiler", "CS2 Installation Not Found! Please install the game or set the path manually with Custom Path!");
        }
    }

    /// <summary>Shows whether the game or its tools were found, in the pill's colour as well as its text.</summary>
    private static void SetStatus(Border pill, TextBlock label, string text, bool found)
    {
        label.Text = text;
        pill.Classes.Set("found", found);
        pill.Classes.Set("missing", !found);
    }

    private static bool IsTextFile(string? path)
    {
        return Path.GetExtension(path)?.Equals(".txt", StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool IsCompiling()
    {
        return compileTask is { IsCompleted: false };
    }

    private string ArgumentBuilder()
    {
        List<string> args = new List<string>();
        string inputFlag = IsTextFile(mappath) ? "-filelist" : "-i";
        string argument = $"-threads {Threads(threadcount)} -fshallow -maxtextureres 256 -dxlevel 110 -quiet -unbufferedio {inputFlag} " + string.Format(null, "\"{0}\"", mappath) + " -noassert ";

        if (buildworld.IsChecked == true)
        {
            args.Add("-world");
            args.Remove("-entities");
        }
        if (builddynamicsurfaceeffects.IsChecked != true)
        {
            args.Add("-skipauxfiles");
        }
        if (buildDeformables.IsChecked == true)
        {
            args.Add("-deformables forced");
        }
        if (entsOnly.IsChecked == true)
        {
            args.Add("-entities");
            args.Remove("-world");
            args.Remove($"-sareverb_threads {Threads(AudioThreadsBox)}");
            args.Remove($"-sareverb_threads {Threads(AudioThreadsBox)}");
            args.Remove($"-sacustomdata_threads {Threads(AudioThreadsBox)}");
        }
        if (settlephys.IsChecked != true)
        {
            args.Add("-nosettle");
        }
        if (debugVisGeo.IsChecked == true)
        {
            args.Add("-debugvisgeo");
        }
        if (onlyBaseTileMesh.IsChecked == true)
        {
            args.Add("-tileMeshBaseGeometry");
        }
        if (genLightmaps.IsChecked == true)
        {
            args.Add("-bakelighting");
            if (oldsource2pre2020 == true)
            {
                args.Add("-vrad3");
                if (compression.IsChecked == true)
                {
                    args.Add("-lightmapCompressionDisabled 0");
                }
            }
            if (cpu.IsChecked == true)
            {
                args.Add("-lightmapcpu");
            }
            args.Add("-lightmapMaxResolution " + lightmapres.SelectedItem);
            args.Add("-lightmapDoWeld");
            args.Add("-lightmapVRadQuality " + lightmapquality.SelectedIndex);
            if (noiseremoval.IsChecked != true)
            {
                args.Add("-lightmapDisableFiltering");
            }
            if (compression.IsChecked != true)
            {
                args.Add("-lightmapCompressionDisabled");
                if (oldsource2pre2020 == true)
                {
                    args.Remove("-lightmapCompressionDisabled 0");
                    args.Add("-lightmapCompressionDisabled 1");
                }
            }
            if (noLightCalc.IsChecked == true)
            {
                args.Add("-disableLightingCalculations");
            }
            if (useDeterCharts.IsChecked == true)
            {
                args.Add("-lightmapDeterministicCharts");
            }
            if (writeDebugPT.IsChecked == true)
            {
                args.Add("-write_debug_path_trace_scene_info");
            }
            if (vrad3LargeSize.IsChecked == true)
            {
                args.Add("-vrad3LargeBlockSize");
            }
            args.Add("-lightmapLocalCompile");
        }
        else if (genLightmaps.IsChecked != true)
        {
            args.Add("-nolightmaps");
        }
        /*if (nolightmaps.Checked)
        {
            args.Add("-nolightmaps");
        }
        if (!nolightmaps.Checked)
        {
            args.Remove("-nolightmaps");
        }*/
        if (buildPhys.IsChecked == true)
        {
            args.Add("-phys");
        }
        if (legacyCompileColMesh.IsChecked == true)
        {
            args.Add("-legacycompilecollisionmesh");
        }
        if (buildVis.IsChecked == true)
        {
            args.Add("-vis");
        }
        if (buildNav.IsChecked == true)
        {
            args.Add("-nav");
        }
        if (navDbg.IsChecked == true)
        {
            args.Add("-navdbg");
        }
        if (gridNav.IsChecked == true)
        {
            args.Add("-gridnav");
        }
        if (saReverb.IsChecked == true)
        {
            args.Add("-sareverb");
            args.Add($"-sareverb_threads {Threads(AudioThreadsBox)}");
            if (oldsource2pre2020 == true)
            {
                args.Remove($"-sareverb_threads {Threads(AudioThreadsBox)}");
            }
        }
        if (baPaths.IsChecked == true)
        {
            args.Add("-sapaths");
            args.Add($"-sareverb_threads {Threads(AudioThreadsBox)}");
            if (oldsource2pre2020 == true)
            {
                args.Remove($"-sareverb_threads {Threads(AudioThreadsBox)}");
            }
        }
        if (bakeCustom.IsChecked == true)
        {
            args.Add("-sacustomdata");
            args.Add($"-sacustomdata_threads {Threads(AudioThreadsBox)}");
        }
        if (vconPrint.IsChecked == true)
        {
            args.Add("-vconsole");
            args.Add("-vconport 29000");
        }
        if (vprofPrint.IsChecked == true)
        {
            args.Add("-resourcecompiler_log_compile_stats");
        }
        if (logPrint.IsChecked == true)
        {
            args.Add("-condebug");
            args.Add("-consolelog");
        }
        if (dangerMode.IsChecked == true)
        {
            args.Add("-danger_mode_ignore_schema_mismatches");
        }
        args.Add("-retail -breakpad -nop4 -outroot ");
        if (oldsource2pre2020 == true)
        {
            args.Add("-retail -breakpad -nompi -nop4 -outroot ");
            args.Remove("-retail -breakpad -nop4 -outroot ");
        }
        return argument + string.Join(" ", args.ToArray());
    }

    private void UpdateArgLabel()
    {
        string myarg = ArgumentBuilder();
        cmdLine.Text = myarg;
    }

    private async void button1_Click(object? sender, RoutedEventArgs e)
    {
        if (IsCompiling())
        {
            await MessageDialog.ShowAsync(this, MessageKind.Warning, "CS2 Map Compiler", "A compilation is already in progress. Please wait for it to complete or cancel it first.");
            return;
        }
        if (string.IsNullOrEmpty(outputpath))
        {
            await MessageDialog.ShowAsync(this, MessageKind.Danger, "CS2 Map Compiler", "No .vmap is specified.");
            return;
        }
        button1.IsEnabled = false;

        if (File.Exists(Path.Combine(outputpath, Path.GetFileNameWithoutExtension(mapname) + ".vpk")))
        {
            if (await MessageDialog.AskAsync(this, MessageKind.Info, "CS2 Map Compiler", "Do you want to overwrite the existing map file?", "Overwrite"))
            {
                arg = ArgumentBuilder() + string.Format(null, "\"{0}\"", outputpath);
                compileTask = ProcessThread();
            }
            else
            {
                Log("(CS2MapCompiler) Compile Cancelled! - " + DateTime.Now + "\n", LogKind.Error);
                button1.IsEnabled = true;
            }
        }
        else
        {
            arg = ArgumentBuilder() + string.Format(null, "\"{0}\"", outputpath);
            compileTask = ProcessThread();
        }
    }

    /// <summary>
    /// Runs resourcecompiler with the arguments built, showing everything it prints in the log, until it exits or is cancelled.
    /// </summary>
    private async Task ProcessThread()
    {
        cancelled = false;
        statusLabel.Text = "Compiling";

        var stopwatch = Stopwatch.StartNew();
        int exitCode;

        process = new Process();

        try
        {
            process.StartInfo.FileName = resourcecompiler;
            process.StartInfo.Arguments = arg;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.OutputDataReceived += OnCompilerOutput;
            process.ErrorDataReceived += OnCompilerOutput;

            //* Start process

            process.Start();
            Log("(CS2MapCompiler) Compile started with parameters:\n " + resourcecompiler + " " + arg + "\nTime: " + DateTime.Now + "\n", LogKind.App);

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    CompilerJob.Add(process);
                }
                catch (Win32Exception exception)
                {
                    Log("(CS2MapCompiler) resourcecompiler will keep running if the app closes during the compile: " + exception.Message + "\n", LogKind.Error);
                }
            }

            if (OperatingSystem.IsWindows() && BakesLightmapsOnGpu() && Vrad3Folder() is { } vrad3Folder)
            {
                lightmapPreview ??= new LightmapPreviewController(this, message => Log("(CS2MapCompiler) " + message + "\n", LogKind.App));
                lightmapPreview.Start(process.Id, vrad3Folder);
            }

            //* Read both outputs asynchronously, line by line, into the log

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Log("(CS2MapCompiler) Could not start resourcecompiler: " + exception.Message + "\n", LogKind.Error);
            statusLabel.Text = "Compile failed to start";
            return;
        }
        finally
        {
            process.Dispose();
            process = null;
            button1.IsEnabled = true;

            if (OperatingSystem.IsWindows())
            {
                lightmapPreview?.Stop();
            }
        }

        if (cancelled)
        {
            statusLabel.Text = "Compile cancelled";
            return;
        }

        Log("(CS2MapCompiler) Compile completed! - " + DateTime.Now + (exitCode == 0 ? "" : $" (exit code {exitCode})") + "\n", LogKind.App);
        statusLabel.Text = (exitCode == 0 ? "Compile completed" : $"Compile exited with code {exitCode}") + $" in {stopwatch.Elapsed:hh\\:mm\\:ss}";
    }

    // The lightmap preview only works with GPU bakes
    private bool BakesLightmapsOnGpu()
    {
        return genLightmaps.IsChecked == true && cpu.IsChecked != true && !oldsource2pre2020;
    }

    // For a map in content\<addons>\<addon> this is game\<addons>\<addon>\_vrad3, and null for maps outside an addon
    private string? Vrad3Folder()
    {
        if (cs2dir == null || mappath == null)
        {
            return null;
        }

        // content\csgo_addons\<addon>\maps\...\<map>.vmap
        for (var folder = Directory.GetParent(mappath); folder?.Parent?.Parent != null; folder = folder.Parent)
        {
            if (folder.Parent.Name.EndsWith("_addons", StringComparison.OrdinalIgnoreCase) && folder.Parent.Parent.Name.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                // cs2dir is game\bin\win64
                var game = Directory.GetParent(cs2dir)!.Parent!.FullName;
                return Path.Combine(game, folder.Parent.Name, folder.Name, "_vrad3");
            }
        }

        return null;
    }

    private void OnCompilerOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            pendingLines.Enqueue(new LogLine(e.Data, LogLine.Classify(e.Data)));
        }
    }

    private void button2_Click(object? sender, RoutedEventArgs e)
    {
        if (IsCompiling())
        {
            cancelled = true;

            try
            {
                // the compiler's own helpers go with it
                process?.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // it exited on its own in the meantime
            }

            Log("(CS2MapCompiler) Compile cancelled! - " + DateTime.Now + "\n", LogKind.Error);
        }
        else
        {
            Log("(CS2MapCompiler) Compile already exited! - " + DateTime.Now + "\n", LogKind.App);
        }
    }

    private async void button3_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the game executable",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Executable Files") { Patterns = ["cs2.exe", "hlvr.exe", "project8.exe", "deadlock.exe", "primelock.exe", "steamtours.exe", "deskjob.exe", "dota2.exe"] }],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } file)
        {
            cs2dir = Path.GetDirectoryName(file);
            await CS2Validator();
            gamedir.Text = cs2dir;
            UpdateArgLabel();
        }
    }

    private void genLightmaps_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (genLightmaps.IsChecked == false)
        {
            cpu.IsEnabled = false;
            lightmapres.IsEnabled = false;
            lightmapquality.IsEnabled = false;
            compression.IsEnabled = false;
            noiseremoval.IsEnabled = false;
            noLightCalc.IsEnabled = false;
            useDeterCharts.IsEnabled = false;
            writeDebugPT.IsEnabled = false;
            vrad3LargeSize.IsEnabled = false;
        }
        else
        {
            cpu.IsEnabled = true;
            lightmapres.IsEnabled = true;
            lightmapquality.IsEnabled = true;
            compression.IsEnabled = true;
            noiseremoval.IsEnabled = true;
            noLightCalc.IsEnabled = true;
            useDeterCharts.IsEnabled = true;
            writeDebugPT.IsEnabled = true;
            vrad3LargeSize.IsEnabled = true;
        }
    }

    /// <summary>Every option rebuilds the command line as it changes.</summary>
    private void Checkers()
    {
        foreach (var control in this.GetLogicalDescendants())
        {
            switch (control)
            {
                case CheckBox box:
                    box.IsCheckedChanged += OnSettingChanged;
                    break;
                case ComboBox combo:
                    combo.SelectionChanged += OnSettingChanged;
                    break;
                case NumericUpDown number:
                    number.ValueChanged += (_, _) => UpdateArgLabel();
                    break;
            }
        }
    }

    /// <summary>The thread count in a box, or all threads while the box is empty, as it is when its text has been cleared.</summary>
    private static int Threads(NumericUpDown box)
    {
        return (int)(box.Value ?? box.Maximum);
    }

    private void OnSettingChanged(object? sender, RoutedEventArgs e)
    {
        UpdateArgLabel();
    }

    private async void button4_Click(object? sender, RoutedEventArgs e)
    {
        if (cs2dir == null)
        {
            await MessageDialog.ShowAsync(this, MessageKind.Warning, "CS2 Map Compiler", "Set the game path with Custom Path first.");
            return;
        }

        string[] addonDirectories = {
            "csgo_addons",
            "hlvr_addons",
            "citadel_addons",
            "dota_addons",
            "testbed_addons",
            "steamtours_addons"
        };

        IStorageFolder? initialDirectory = null;

        foreach (string addonDir in addonDirectories)
        {
            string path = Path.Combine(Directory.GetParent(cs2dir)!.Parent!.Parent!.FullName, "content", addonDir);
            if (Directory.Exists(path))
            {
                initialDirectory = await StorageProvider.TryGetFolderFromPathAsync(path);
                break;
            }
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open .vmap",
            AllowMultiple = false,
            SuggestedStartLocation = initialDirectory,
            FileTypeFilter =
            [
                new FilePickerFileType("Hammer Map File") { Patterns = ["*.vmap"] },
                new FilePickerFileType("Map List") { Patterns = ["*.txt"] },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } file)
        {
            mappath = file;
            mapname = Path.GetFileName(file);
            addonname = Directory.GetParent(file)!.Parent!.Name;
            outputpath = Directory.GetParent(cs2dir)!.Parent!.FullName;
            mapLabel.Text = mappath;
            outputdir.Text = outputpath;
            button5.IsEnabled = true;
            UpdateArgLabel();
        }
    }

    private async void button5_Click(object? sender, RoutedEventArgs e)
    {
        string[] addonDirectories = {
            "csgo_addons",
            "hlvr_addons",
            "citadel_addons",
            "dota_addons",
            "testbed_addons",
            "steamtours_addons"
        };

        IStorageFolder? initialDirectory = null;

        foreach (string addonDir in addonDirectories)
        {
            string path = Path.Combine(Directory.GetParent(cs2dir!)!.Parent!.FullName, addonDir, addonname!, "maps");
            if (Directory.Exists(path))
            {
                initialDirectory = await StorageProvider.TryGetFolderFromPathAsync(path);
                break;
            }
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Change Output",
            AllowMultiple = false,
            SuggestedStartLocation = initialDirectory,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folder)
        {
            outputpath = folder;
            outputdir.Text = outputpath;
        }
    }

    private void entsOnly_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (entsOnly.IsChecked == false)
        {
            genLightmaps.IsEnabled = true;
            cpu.IsEnabled = true;
            lightmapres.IsEnabled = true;
            lightmapquality.IsEnabled = true;
            noiseremoval.IsEnabled = true;
            compression.IsEnabled = true;
            useDeterCharts.IsEnabled = true;
            noLightCalc.IsEnabled = true;
            writeDebugPT.IsEnabled = true;
            buildPhys.IsEnabled = true;
            buildVis.IsEnabled = true;
            buildNav.IsEnabled = true;
            navDbg.IsEnabled = true;
            saReverb.IsEnabled = true;
            baPaths.IsEnabled = true;
            bakeCustom.IsEnabled = true;
        }
    }

    private void PresetFullBuild_Click(object? sender, RoutedEventArgs e)
    {
        //World
        buildworld.IsChecked = true;
        entsOnly.IsChecked = false;
        settlephys.IsChecked = true;
        debugVisGeo.IsChecked = false;
        onlyBaseTileMesh.IsChecked = false;
        builddynamicsurfaceeffects.IsChecked = true;
        buildDeformables.IsChecked = false;
        //Baked Lighting
        genLightmaps.IsEnabled = true;
        genLightmaps.IsChecked = true;
        cpu.IsChecked = false;
        nolightmaps.IsChecked = false;
        lightmapres.SelectedIndex = 3;
        lightmapquality.SelectedIndex = 1;
        vrad3LargeSize.IsChecked = true;
        compression.IsChecked = true;
        noiseremoval.IsChecked = true;
        noLightCalc.IsChecked = false;
        useDeterCharts.IsChecked = false;
        writeDebugPT.IsChecked = false;
        //Phys
        buildPhys.IsChecked = true;
        legacyCompileColMesh.IsChecked = false;
        //Vis
        buildVis.IsChecked = true;
        //Nav
        buildNav.IsChecked = true;
        if (gridNav.IsEnabled)
        {
            gridNav.IsChecked = true;
        }
        navDbg.IsChecked = false;
        //Steam Audio
        saReverb.IsChecked = true;
        baPaths.IsChecked = true;
        bakeCustom.IsChecked = false; //yet
        //Extra
        vconPrint.IsChecked = false;
        vprofPrint.IsChecked = false;
        logPrint.IsChecked = false;
        dangerMode.IsChecked = false;
    }

    private void PresetFastBuild_Click(object? sender, RoutedEventArgs e)
    {
        //World
        buildworld.IsChecked = true;
        entsOnly.IsChecked = false;
        settlephys.IsChecked = true;
        debugVisGeo.IsChecked = false;
        onlyBaseTileMesh.IsChecked = false;
        builddynamicsurfaceeffects.IsChecked = true;
        buildDeformables.IsChecked = false;
        //Baked Lighting
        genLightmaps.IsChecked = false;
        genLightmaps.IsEnabled = false;
        //Phys
        buildPhys.IsChecked = true;
        legacyCompileColMesh.IsChecked = false;
        //Vis
        buildVis.IsChecked = false;
        //Nav
        buildNav.IsChecked = true;
        if (gridNav.IsEnabled)
        {
            gridNav.IsChecked = true;
        }
        navDbg.IsChecked = false;
        //Steam Audio
        saReverb.IsChecked = false;
        baPaths.IsChecked = false;
        bakeCustom.IsChecked = false; //yet
        //Extra
        vconPrint.IsChecked = false;
        vprofPrint.IsChecked = false;
        logPrint.IsChecked = false;
        dangerMode.IsChecked = false;
    }

    private void PresetFinalBuild_Click(object? sender, RoutedEventArgs e)
    {
        //World
        buildworld.IsChecked = true;
        entsOnly.IsChecked = false;
        settlephys.IsChecked = true;
        debugVisGeo.IsChecked = false;
        onlyBaseTileMesh.IsChecked = false;
        builddynamicsurfaceeffects.IsChecked = true;
        buildDeformables.IsChecked = false;
        //Baked Lighting
        genLightmaps.IsEnabled = true;
        genLightmaps.IsChecked = true;
        cpu.IsChecked = false;
        nolightmaps.IsChecked = false;
        lightmapres.SelectedIndex = 2;
        lightmapquality.SelectedIndex = 2;
        vrad3LargeSize.IsChecked = true;
        compression.IsChecked = true;
        noiseremoval.IsChecked = true;
        noLightCalc.IsChecked = false;
        useDeterCharts.IsChecked = false;
        writeDebugPT.IsChecked = false;
        //Phys
        buildPhys.IsChecked = true;
        legacyCompileColMesh.IsChecked = false;
        //Vis
        buildVis.IsChecked = true;
        //Nav
        buildNav.IsChecked = true;
        if (gridNav.IsEnabled)
        {
            gridNav.IsChecked = true;
        }
        navDbg.IsChecked = false;
        //Steam Audio
        saReverb.IsChecked = true;
        baPaths.IsChecked = true;
        bakeCustom.IsChecked = false;
        //Extra
        vconPrint.IsChecked = false;
        vprofPrint.IsChecked = false;
        logPrint.IsChecked = false;
        dangerMode.IsChecked = false;
    }

    private void PresetOnlyEntities_Click(object? sender, RoutedEventArgs e)
    {
        //World
        buildworld.IsChecked = true;
        entsOnly.IsChecked = true;
        settlephys.IsChecked = true;
        debugVisGeo.IsChecked = false;
        onlyBaseTileMesh.IsChecked = false;
        builddynamicsurfaceeffects.IsChecked = true;
        buildDeformables.IsChecked = false;
        //Baked Lighting
        genLightmaps.IsChecked = false;
        genLightmaps.IsEnabled = false;
        //Phys
        buildPhys.IsChecked = false;
        legacyCompileColMesh.IsChecked = false;
        //Vis
        buildVis.IsChecked = false;
        //Nav
        buildNav.IsChecked = false;
        if (gridNav.IsEnabled)
        {
            gridNav.IsChecked = false;
        }
        navDbg.IsChecked = false;
        //Steam Audio
        saReverb.IsChecked = false;
        baPaths.IsChecked = false;
        bakeCustom.IsChecked = false; //yet
        //Extra
        vconPrint.IsChecked = false;
        vprofPrint.IsChecked = false;
        logPrint.IsChecked = false;
        dangerMode.IsChecked = false;
    }

    private void PresetCustom_Click(object? sender, RoutedEventArgs e)
    {
        //World
        buildworld.IsChecked = true;
        entsOnly.IsChecked = false;
        settlephys.IsChecked = true;
        debugVisGeo.IsChecked = false;
        onlyBaseTileMesh.IsChecked = false;
        builddynamicsurfaceeffects.IsChecked = true;
        buildDeformables.IsChecked = false;
        //Baked Lighting
        genLightmaps.IsEnabled = true;
        genLightmaps.IsChecked = true;
        cpu.IsChecked = false;
        nolightmaps.IsChecked = false;
        lightmapres.SelectedIndex = 3;
        lightmapquality.SelectedIndex = 1;
        vrad3LargeSize.IsChecked = true;
        compression.IsChecked = true;
        noiseremoval.IsChecked = true;
        noLightCalc.IsChecked = false;
        useDeterCharts.IsChecked = false;
        writeDebugPT.IsChecked = false;
        //Phys
        buildPhys.IsChecked = true;
        legacyCompileColMesh.IsChecked = false;
        //Vis
        buildVis.IsChecked = true;
        //Nav
        buildNav.IsChecked = true;
        if (gridNav.IsEnabled)
        {
            gridNav.IsChecked = true;
        }
        navDbg.IsChecked = false;
        //Steam Audio
        saReverb.IsChecked = true;
        baPaths.IsChecked = true;
        bakeCustom.IsChecked = false;
        //Extra
        vconPrint.IsChecked = true;
        vprofPrint.IsChecked = true;
        logPrint.IsChecked = true;
        dangerMode.IsChecked = false;
    }

    private readonly Dictionary<string, string> _helpText = new Dictionary<string, string>
    {
        {"labelThreads", "Amount of CPU threads used by the compiler."},
        {"labelCancel", "Cancel build."},
        {"labelCustomPath", "Override game path."},
        {"labelgamestatus", "Current game status. Game executable must be present."},
        {"labeltoolstatus", "Current tools status. resourcecompiler.exe must be present."},
        {"labeloverrideoutput", "Override map vpk output path."},
        {"labelopenvmap", "Open .vmap file."},
        {"labelCompile", "Begin map compilation."},
        {"labelBuildWorld", "Build world."},
        {"labelEntsOnly", "Compile only entities. Useful for testing small changes."},
        {"labelSettlePhys", "Pre-Settle physics objects."},
        {"labelDebugVisGeo", "Debug VIS Geometry."},
        {"labelOnlyBaseTileMesh", "Only base Tile Mesh geometry."},
        {"labelDynamicSurfaceEffects", "Build world dynamic surface effects. Unknown."},
        {"labelDeformable", "Build deformable geometry. Unknown."},
        {"labelgenLightmaps", "Bake lightmaps. GPU with RT support required."},
        {"labellightmapres", "Lightmap resolution. 1024 - Standard, 2048 - Final, 8192 - Shipping / Final."},
        {"labellightmapquality", "Lightmap quality."},
        {"labelgenLightmapsAlyx", "Bake lightmaps. Uses CPU for compilation."},
        {"labelCPUcompile", "Use CPU for lightmap compilation. Removed in CS2 after Oct 3, 2024."},
        {"labelCompression", "Enable/Disable lightmap compression."},
        {"labelnoiseremoval", "Enable/Disable lightmap denoising."},
        {"labelnoLightCalc", "Disable lighting calculations (useful for debugging texel density/chart allocation)."},
        {"labeluseDeterCharts", "Use Deterministic lightmap charts during bake."},
        {"labellargesize", "Make larger VRAD3 blocks at the cost of higher VRAM usage."},
        {"labelwriteDebugPT", "Write debug Path Trace scene info into a file."},
        {"labelbuildPhys", "Build collision physics mesh."},
        {"labellegacyCompileColMesh", "Build legacy collision physics mesh."},
        {"labelbuildVis", "Build visibility for optimization. Must be set to On in shipping maps."},
        {"labelbuildNav", "Build navigation mesh for NPCs / CS Bots."},
        {"labelgridNav", "Build grid navigation mesh for Dota NPCs."},
        {"labelnavDbg", "Save nav debug stages to file."},
        {"labelsaReverb", "Build Steam Audio reverb data."},
        {"labelsaPaths", "Build Steam Audio pathing data."},
        {"labelsaThreads", "CPU threads used for Steam Audio build."},
        {"labelbakeCustom", "Build Steam Audio custom data (occlusions and materials)."},
        {"labelvconPrint", "Print resourcecompiler data to VConsole (Default port 29000)"},
        {"labelvprofPrint", "Print VProf stats at the end of compilation."},
        {"labellogPrint", "Save resourcecompiler log to console.log in game/mod."},
        {"labelfullbuild", "Standard compile of all map components"},
        {"labelfastbuild", "Build world, phys or nav but no vis or lighting"},
        {"labelfinalbuild", "Build everything, including final quality lighting"},
        {"labelentsonly", "Build Entities. Nothing else!"},
        {"labelcustom", "Custom"},
        {"labeldangerMode", "Ignore Schema mismatches."},
        //{"labelcompilestatus", "Compilation status."},
    };

    /// <summary>Every control tagged with a help text shows it as a tooltip, disabled ones too so they still say what they are.</summary>
    private void HelpSystemEventReg()
    {
        foreach (var control in this.GetLogicalDescendants().OfType<Control>())
        {
            if (control.Tag is string tag && _helpText.TryGetValue(tag, out string? helpText))
            {
                ToolTip.SetTip(control, helpText);
                ToolTip.SetShowOnDisabled(control, true);
            }
        }
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        await new SettingsWindow().ShowDialog(this);
    }

    /// <summary>Adds the app's own message to the log, a line for each of its lines.</summary>
    private void Log(string text, LogKind kind)
    {
        foreach (var line in text.Split('\n'))
        {
            pendingLines.Enqueue(new LogLine(line, kind));
        }
    }

    /// <summary>Shows the lines printed since the last time, following them down when the log was already at its end.</summary>
    private void FlushLog()
    {
        if (pendingLines.IsEmpty)
        {
            return;
        }

        logScroller ??= logList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        var atEnd = logScroller == null || logScroller.Offset.Y >= logScroller.Extent.Height - logScroller.Viewport.Height - 4;

        while (pendingLines.TryDequeue(out var line))
        {
            logLines.Add(line);
        }

        if (atEnd)
        {
            logList.ScrollIntoView(logLines.Count - 1);
        }
    }

    private async void OnCopyLog(object? sender, RoutedEventArgs e)
    {
        var lines = logList.SelectedItems is { Count: > 0 } selected ? logLines.Where(selected.Contains) : logLines;

        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines.Select(line => line.Text)));
        }
    }

    private void OnClearLog(object? sender, RoutedEventArgs e)
    {
        logLines.Clear();
    }
}
