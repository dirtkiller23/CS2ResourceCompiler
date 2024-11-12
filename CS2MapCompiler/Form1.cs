using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2MapCompiler
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        string cs2dir;
        string resourcecompiler;
        string addonname;
        string mapname;
        string mappath;
        string outputpath;
        string arg;
        bool sourcetwentytwenty = false;
        static string output;
        Process process;
        private string GetCS2Dir()
        {
            string steamPath = (string)Registry.GetValue("HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath", "");
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
                    libraries.Add(match.Replace("\\\\", "\\"));
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
        private void Form1_Load(object sender, EventArgs e)
        {
            lightmapres.SelectedIndex = 2;
            lightmapquality.SelectedIndex = 1;
            cs2dir = GetCS2Dir();
            CS2Validator();
            gamedir.Text = cs2dir;


            int cpuCount = Environment.ProcessorCount;
            for (int i = 1; i <= cpuCount; i++)
            {
                threadcount.Items.Add(i);
                AudioThreadsBox.Items.Add(i);
            }
            AudioThreadsBox.SelectedIndex = cpuCount - 1;
            threadcount.SelectedIndex = cpuCount - 1;
        }
        void CS2Validator()
        {
            string[] requiredExecutables = { "cs2.exe", "hlvr.exe", "project8.exe", "steamtours.exe", "dota2.exe", "deskjob.exe" };
            bool anyExecutableFound = false;

            foreach (string exe in requiredExecutables)
            {
                if (File.Exists(Path.Combine(cs2dir,exe)))
                {
                    anyExecutableFound = true;
                    cs2status.Text = $"Found {exe}";
                    cs2status.ForeColor = Color.Green;
                    button1.Enabled = true;
                    if (exe != "cs2.exe" && exe != "project8.exe" && exe != "dota2.exe")
                    {
                        sourcetwentytwenty = true;                                            
                    }

                    if (sourcetwentytwenty == true)
                    {
                        cpu.Enabled = false;
                        cpu.Visible = false;
                        legacyCompileColMesh.Enabled = false;
                        legacyCompileColMesh.Visible = false;
                        bakeCustom.Enabled = false;
                        bakeCustom.Visible = false;
                        AudioThreadsBox.Visible = false;
                        AudioThreadsBox.Enabled = false;
                        AudioThreadsLabel.Visible = false;
                        AudioThreadsLabel.Enabled = false;
                        cpuLabel.Text = "Only CPU lightmap is supported.";
                    }

                    if (exe !="dota2.exe")
                    {
                        gridNav.Enabled = false;
                        gridNav.Visible = false;
                        nolightmaps.Enabled = false;
                        nolightmaps.Visible = false;
                    } else
                    if (exe == "dota2.exe")
                    {
                        gridNav.Enabled = true;
                        gridNav.Visible = true;
                        genLightmaps.Checked = false;
                        noiseremoval.Checked = false;
                        buildNav.Checked = false;
                        saReverb.Checked = false;
                        baPaths.Checked = false;
                        bakeCustom.Checked = false;
                    }

                    if (File.Exists(Path.Combine(cs2dir, "resourcecompiler.exe")))
                    {
                        wststatus.Text = "Found";
                        wststatus.ForeColor = Color.Green;
                        resourcecompiler = Path.Combine(cs2dir, "resourcecompiler.exe");
                        button1.Enabled = true;
                    }
                    else
                    {
                        wststatus.Text = "Not Found";
                        wststatus.ForeColor = Color.DarkRed;
                        MessageBox.Show("Please Install Workshop Tools!", "CS2 Map Compiler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        button1.Enabled = false;
                    }

                    break; // Exit the loop once any executable is found
                }
            }

            if (!anyExecutableFound)
            {
                cs2status.Text = "Not Found";
                cs2status.ForeColor = Color.DarkRed;
                wststatus.Text = "Not Found";
                wststatus.ForeColor = Color.DarkRed;
                MessageBox.Show("CS2 Installation Not Found! Please install the game or set the path manually in the options!", "CS2 Map Compiler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button1.Enabled = false;
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            
        }

        private void webBrowser1_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {

        }
            
        string ArgumentBuilder()
        {
            List<string> args = new List<string>();
            string argument = $"-threads {threadcount.SelectedItem} -fshallow -maxtextureres 256 -dxlevel 110 -quiet -unbufferedio -i " + string.Format("\"{0}\"",mappath) + " -noassert ";
            
            if(buildworld.Checked)
            {
                args.Add("-world");
                args.Remove("-entities");
            }
            if (entsOnly.Checked)
            {             
                args.Add("-entities");
                args.Remove("-world");              
                args.Remove($"-sareverb_threads {AudioThreadsBox.SelectedItem}");
                args.Remove($"-sareverb_threads {AudioThreadsBox.SelectedItem}");
                args.Remove($"-sacustomdata_threads {AudioThreadsBox.SelectedItem}");
            }
            if (!settlephys.Checked)
            {
                args.Add("-nosettle");
            }
            if (debugVisGeo.Checked)
            {
                args.Add("-debugvisgeo");
            }
            if (onlyBaseTileMesh.Checked)
            {
                args.Add("-tileMeshBaseGeometry");
            }
            if (genLightmaps.Checked)
            {
                args.Add("-bakelighting");
                if (sourcetwentytwenty == true)
                {
                    args.Add("-vrad3");
                    if (compression.Checked)
                    {
                        args.Add("-lightmapCompressionDisabled 0");
                    }
                }
                if (cpu.Checked)
                {
                    args.Add("-lightmapcpu");
                }
                args.Add("-lightmapMaxResolution " + lightmapres.Text);
                args.Add("-lightmapDoWeld");
                args.Add("-lightmapVRadQuality " + lightmapquality.SelectedIndex.ToString());
                if(!noiseremoval.Checked)
                {
                    args.Add("-lightmapDisableFiltering");
                }              
                if (!compression.Checked)
                {
                    args.Add("-lightmapCompressionDisabled");
                    if (sourcetwentytwenty == true)
                    {
                        args.Remove("-lightmapCompressionDisabled 0");
                        args.Add("-lightmapCompressionDisabled 1");
                    }
                }
                if (noLightCalc.Checked)
                {
                    args.Add("-disableLightingCalculations");
                }
                if (useDeterCharts.Checked)
                {
                    args.Add("-lightmapDeterministicCharts");
                }
                if (writeDebugPT.Checked)
                {
                    args.Add("-write_debug_path_trace_scene_info");
                }               
                args.Add("-lightmapLocalCompile");             
            }
            else if(!genLightmaps.Checked)
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
            if(buildPhys.Checked)
            {
                args.Add("-phys");
            }
            if (legacyCompileColMesh.Checked)
            {
                args.Add("-legacycompilecollisionmesh");
            }
            if (buildVis.Checked)
            {
                args.Add("-vis");
            }
            if (buildNav.Checked)
            {
                args.Add("-nav");
            }
            if (navDbg.Checked)
            {
                args.Add("-navdbg");
            }
            if (gridNav.Checked)
            {
                args.Add("-gridnav");
            }
            if (saReverb.Checked)
            {
                args.Add("-sareverb");
                args.Add($"-sareverb_threads {AudioThreadsBox.SelectedItem}");
                if (sourcetwentytwenty == true)
                {
                    args.Remove($"-sareverb_threads {AudioThreadsBox.SelectedItem}");
                }
            }
            if (baPaths.Checked)
            {
                args.Add("-sapaths");
                args.Add($"-sareverb_threads {AudioThreadsBox.SelectedItem}");
                if (sourcetwentytwenty == true)
                {
                    args.Remove($"-sareverb_threads {AudioThreadsBox.SelectedItem}");
                }
            }
            if (bakeCustom.Checked)
            {
                args.Add("-sacustomdata");
                args.Add($"-sacustomdata_threads {AudioThreadsBox.SelectedItem}");
            }
            if (vconPrint.Checked)
            {
                args.Add("-vconsole");
                args.Add("-vconport 29000");
            }
            if (vprofPrint.Checked)
            {
                args.Add("-resourcecompiler_log_compile_stats");
            }
            if (logPrint.Checked)
            {
                args.Add("-condebug");
                args.Add("-consolelog");
            }           
            args.Add("-retail -breakpad -nop4 -outroot ");
            if (sourcetwentytwenty == true)
            {
                args.Add("-retail -breakpad -nompi -nop4 -outroot ");
                args.Remove("-retail -breakpad -nop4 -outroot ");
            }             
            return argument + string.Join(" ",args.ToArray());   
            
        }

        private void button1_Click(object sender, EventArgs e)
        {
            process = new Process();
            var task = new Task(() => ProcessThread());
            if (File.Exists(Path.Combine(outputpath, Path.GetFileNameWithoutExtension(mapname) + ".vpk")))
            {
                var response = MessageBox.Show("Do you want to overwrite the existing map file?", "CS2 Map Compiler", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (response == DialogResult.Yes)
                {
                    arg = ArgumentBuilder() + string.Format("\"{0}\"", outputpath);
                    task.Start();
                }
                else
                {
                    MessageBox.Show("Compile Cancelled!", "CS2 Map Compiler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                arg = ArgumentBuilder() + string.Format("\"{0}\"", outputpath);
                task.Start();
            }
        }
        
        private void ProcessThread()
        {
            
            process.StartInfo.FileName = resourcecompiler;
            process.StartInfo.Arguments = arg;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardError = true;
            
            //* Set ONLY ONE handler here.
            
            //* Start process

            process.Start();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("(CS2MapCompiler) Compile started with parameters:\n " + resourcecompiler +" "+ arg + "\nTime: " + DateTime.Now + "\n");
            Console.ForegroundColor = ConsoleColor.White;
            //* Read one element asynchronously
            //* Read the other one synchronously

            output = process.StandardError.ReadToEnd();
            Console.WriteLine(output);
            
            process.WaitForExit();
            
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
                process.Dispose();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("(CS2MapCompiler) Compile cancelled! - " + DateTime.Now + "\n");
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("(CS2MapCompiler) Compile already exited! - " + DateTime.Now + "\n");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            OpenFileDialog file = new OpenFileDialog();
            file.Filter = "Executable Files (*.exe)|cs2.exe;hlvr.exe;project8.exe;steamtours.exe;deskjob.exe;dota2.exe";
            if (file.ShowDialog() == DialogResult.OK)
            {
                cs2dir = Path.GetDirectoryName(file.FileName);
                CS2Validator();
                gamedir.Text = cs2dir;              
            }
        }

        private void genLightmaps_CheckedChanged(object sender, EventArgs e)
        {
            if(genLightmaps.Checked == false)
            {

                cpu.Enabled = false;
                lightmapres.Enabled = false;
                lightmapquality.Enabled = false;
                compression.Enabled = false;
                noiseremoval.Enabled = false;
                noLightCalc.Enabled = false;
                useDeterCharts.Enabled = false;
                writeDebugPT.Enabled = false;
                /*nolightmaps.Enabled = true;
                nolightmaps.Visible = false;*/
            }
            else
            {
                /*nolightmaps.Enabled = false;
                nolightmaps.Visible = false;*/
                cpu.Enabled = true;
                lightmapres.Enabled = true;
                lightmapquality.Enabled = true;
                compression.Enabled = true;
                noiseremoval.Enabled = true;
                noLightCalc.Enabled = true;
                useDeterCharts.Enabled = true;
                writeDebugPT.Enabled = true;
            }

        }

        private void button4_Click(object sender, EventArgs e)
        {
            OpenFileDialog file = new OpenFileDialog();

            string[] addonDirectories = {
        "csgo_addons",
        "hlvr_addons",
        "citadel_addons",
        "dota_addons",
        "testbed_addons",
        "steamtours_addons"
        };

            foreach (string addonDir in addonDirectories)
            {
                string path = Path.Combine(Directory.GetParent(cs2dir).Parent.Parent.FullName, "content", addonDir);
                if (Directory.Exists(path))
                {
                    file.InitialDirectory = path;
                    break;
                }
            }

            file.Filter = "Hammer Map File |*.vmap";

            if (file.ShowDialog() == DialogResult.OK)
            {
                mappath = file.FileName;
                mapname = Path.GetFileName(file.FileName);
                addonname = Directory.GetParent(file.FileName).Parent.Name;
                outputpath = Directory.GetParent(cs2dir).Parent.FullName;
                outputdir.Text = outputpath;
                button5.Enabled = true;
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            string dummyFileName = "Select Here";
            SaveFileDialog file = new SaveFileDialog();

        string[] addonDirectories = {
        "csgo_addons",
        "hlvr_addons",
        "citadel_addons",
        "dota_addons",
        "testbed_addons",
        "steamtours_addons"
        };

            foreach (string addonDir in addonDirectories)
            {
                string path = Path.Combine(Directory.GetParent(cs2dir).Parent.FullName, addonDir, addonname, "maps");
                if (Directory.Exists(path))
                {
                    file.InitialDirectory = path;
                    break;
                }
            }

            file.FileName = dummyFileName;
            file.Filter = "Directory | directory";

            if (file.ShowDialog() == DialogResult.OK)
            {
                outputpath = Path.GetDirectoryName(file.FileName);
                outputdir.Text = outputpath;
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            Console.Clear();
            Console.WriteLine(ArgumentBuilder() + string.Format("\"{0}\"", outputpath));
        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void outputdir_Click(object sender, EventArgs e)
        {

        }

        private void entsOnly_CheckedChanged(object sender, EventArgs e)
        {
            if (entsOnly.Checked == false)
            {
                genLightmaps.Enabled = true;
                cpu.Enabled = true;
                lightmapres.Enabled = true;
                lightmapquality.Enabled = true;
                noiseremoval.Enabled = true;
                compression.Enabled = true;
                useDeterCharts.Enabled = true;
                noLightCalc.Enabled = true;
                writeDebugPT.Enabled = true;
                buildPhys.Enabled = true;
                buildVis.Enabled = true;
                buildNav.Enabled = true;
                navDbg.Enabled = true;
                saReverb.Enabled = true;
                baPaths.Enabled = true;
                bakeCustom.Enabled = true;
            }
            else
            {              
            }
        }
    }
}
