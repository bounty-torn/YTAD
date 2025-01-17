using AngleSharp.Media;
using System.Diagnostics.Metrics;
using System.Text;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;
using System.Windows.Forms;
using TagLib;
using System.Diagnostics;
using Microsoft.VisualBasic;
using Fastenshtein;

namespace YTPD
{
    public partial class Form1 : Form
    {
        public double prog = 0;
        public static bool isConverting = false;
        public static bool isPaused = false;
        public static bool isSaving = false;
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // set up saved data
            txt_Dir.Text = Properties.Settings.Default.Directory;
            LoadCSVIntoDataGridView();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;
            var youtube = new YoutubeClient();
            string album = "";
            string thumb = "";

            // You can specify either the video URL or its ID
            try
            {
                var albuminfo = await youtube.Playlists.GetAsync(txt_URL.Text);
                album = albuminfo.Title;
                thumb = albuminfo.Thumbnails[1].Url;
            }
            catch
            {
                MessageBox.Show("Unable to add album. Please copy/paste again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button1.Enabled = true;
                return;
            }

            album = album.Replace("Album - ", "");
            album = album.Replace("- Topic", "");

            // Get all playlist videos
            Int16 songnum = 1;
            await foreach (var video in youtube.Playlists.GetVideosAsync(txt_URL.Text))
            {
                var title = video.Title;
                var band = video.Author.ChannelTitle;
                var author = video.Author;
                var duration = video.Duration;
                var link = video.Url;

                band = band.Replace("- Topic", "").Trim();

                dgv_downloads.Rows.Add(band, album, songnum.ToString(), title, duration, link, "0", "0", "No");
                songnum++;
            }

            dgv_downloads.Refresh();
            txt_URL.Clear();
            txt_URL.Focus();

            SaveDataGridViewToCSV();
            button1.Enabled = true;
        }

        private async void timer1_Tick(object sender, EventArgs e)
        {
            // ensure a directory exists
            if (!Directory.Exists(txt_Dir.Text))
            {
                MessageBox.Show("Directory does not exist! Please browse for a new directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                timer1.Enabled = false;
                return;
            }

            // turn it off while it's running
            timer1.Enabled = false;

            string artist = "";
            string album = "";
            string songnum = "";
            string song = "";
            string duration = "";
            string link = "";

            foreach (DataGridViewRow row in dgv_downloads.Rows)
            {
                // if the user paused downloading, then go away
                if (isPaused) return;

                if (row.Cells[0].Value == null || row.Cells[0].Value.ToString().Length < 1)
                {
                    try
                    {
                        dgv_downloads.Rows.RemoveAt(row.Index);
                    }
                    catch
                    {
                        break;
                    }

                    continue;
                }

                if (row.Cells[0].Value.ToString().ToLower().Contains("romeo"))
                {
                    song = "";
                }

                Int16 dlpercent = Convert.ToInt16(row.Cells["DL"].Value);

                if (dlpercent > 0 && dlpercent < 100) return;

                if (dlpercent == 0)
                {
                    artist = row.Cells["Artist"].Value.ToString();
                    album = row.Cells["Album"].Value.ToString();
                    songnum = row.Cells["SongNum"].Value.ToString();
                    song = row.Cells["Song"].Value.ToString();
                    duration = row.Cells["Duration"].Value.ToString();
                    link = row.Cells["Link"].Value.ToString();

                    row.Cells["DL"].Value = "1";

                    // create artist directory
                    if (!Directory.Exists(txt_Dir.Text + "\\" + artist))
                    {
                        Directory.CreateDirectory(txt_Dir.Text + "\\" + GetValidFilename(artist));
                    }

                    // create album directory
                    if (!Directory.Exists(txt_Dir.Text + "\\" + artist + "\\" + album))
                    {
                        Directory.CreateDirectory(txt_Dir.Text + "\\" + GetValidFilename(artist) + "\\" + GetValidFilename(album));
                    }

                    // highlight the row being used
                    dgv_downloads.Rows[row.Index].DefaultCellStyle.BackColor = Color.SteelBlue;
                    dgv_downloads.Rows[row.Index].DefaultCellStyle.ForeColor = Color.Azure;
                    dgv_downloads.FirstDisplayedScrollingRowIndex = row.Index;

                    // get the manifest information
                    string fullpath = "";
                    try
                    {
                        var youtube = new YoutubeClient();
                        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(link);
                        var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                        // set the path
                        fullpath = Path.Combine(txt_Dir.Text + "\\" + GetValidFilename(artist) + "\\" + GetValidFilename(album), songnum + " - " + GetValidFilename(song) + "." + streamInfo.Container);

                        if (System.IO.File.Exists(fullpath))
                        {
                            row.Cells["DL"].Value = "100";
                            break;
                        }

                        var progress = new Progress<double>(percentage =>
                        {
                            row.Cells["DL"].Value = Math.Round(percentage * 100, 0);
                        });

                        // actual stream
                        var stream = await youtube.Videos.Streams.GetAsync(streamInfo);
                        await youtube.Videos.Streams.DownloadAsync(streamInfo, fullpath, progress);

                        dgv_downloads.Rows[row.Index].DefaultCellStyle.BackColor = Color.Azure;
                        dgv_downloads.Rows[row.Index].DefaultCellStyle.ForeColor = Color.Black;
                    }
                    catch
                    {
                        row.Cells["DL"].Value = "100";
                        dgv_downloads.Rows[row.Index].DefaultCellStyle.BackColor = Color.DarkRed;
                        dgv_downloads.Rows[row.Index].DefaultCellStyle.ForeColor = Color.White;
                    }
                    finally
                    {
                        if (!isConverting && fullpath.Length > 1)
                        {
                            isConverting = true;
                            await ConvertFile(fullpath, fullpath.Substring(fullpath.LastIndexOf('.')));
                            Console.WriteLine(fullpath);
                            isConverting = false;
                        }
                    }

                    SaveDataGridViewToCSV();

                    break;
                }
            }

            // turn timer back on when done
            timer1.Enabled = true;
        }

        static async Task ConvertFile(string inputFilePath, string fileExt)
        {
            string outputFilePath = inputFilePath.Replace(fileExt, ".mp3");
            string ffmpegcom = $"-n -i \"{inputFilePath}\" \"{outputFilePath}\"";

            // Setup parameters
            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = "ffmpeg",
                Arguments = ffmpegcom,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            // Start the process
            using (Process process = new Process { StartInfo = psi })
            {
                process.Start();

                // Wait for the process to exit
                await Task.Run(() => process.WaitForExit());

                // Check the exit code
                int exitCode = process.ExitCode;

                // Handle the result based on the exit code
                if (exitCode == 0)
                {
                    Console.WriteLine("Conversion completed successfully.");
                    await Task.Run(() => System.IO.File.Delete(inputFilePath));
                }
                else
                {
                    Console.WriteLine($"Error: {exitCode}");
                }
            }
        }

        private void timer_tag_Tick(object sender, EventArgs e)
        {
            string[] artist;
            string album = "";
            string songnum = "";
            string song = "";
            string duration = "";
            string link = "";

            foreach (DataGridViewRow row in dgv_downloads.Rows)
            {
                if (row.Cells[0].Value == null || row.Cells[0].Value == "") return;

                Int16 dlpercent = Convert.ToInt16(row.Cells["DL"].Value);
                if (dlpercent == 100 && row.Cells["Tagged"].Value.ToString() == "0")
                {
                    artist = new[] { row.Cells["Artist"].Value.ToString() };
                    album = row.Cells["Album"].Value.ToString();
                    songnum = row.Cells["SongNum"].Value.ToString();
                    song = row.Cells["Song"].Value.ToString();
                    duration = row.Cells["Duration"].Value.ToString();
                    link = row.Cells["Link"].Value.ToString();

                    // actual stream
                    string fullpath = Path.Combine(txt_Dir.Text + "\\" + GetValidFilename(row.Cells["Artist"].Value.ToString()) + "\\" + GetValidFilename(album), songnum + " - " + GetValidFilename(song) + ".mp3");

                    // does the file exist? If not, marked it tagged as the user probably moved it already
                    if (!System.IO.File.Exists(fullpath))
                    {
                        continue;
                    }

                    // check if file is in use
                    bool fileInUse = true;
                    while (fileInUse)
                    {
                        try
                        {
                            // Attempt to open the file with FileShare.None to check if it's in use
                            using (var fileStream = new FileStream(fullpath, FileMode.Open, FileAccess.Read, FileShare.None))
                            {
                                // if the file can be opened, then let's write the mp3 tags
                                fileInUse = false;
                            }
                        }
                        catch (IOException)
                        {
                            // File is still in use, wait for a short duration before trying again
                            continue;
                        }
                    }

                    var tagfile = TagLib.File.Create(fullpath);

                    tagfile.Tag.AlbumArtists = artist;
                    tagfile.Tag.Artists = artist;
                    tagfile.Tag.Album = album;
                    tagfile.Tag.Title = song;
                    tagfile.Tag.Comment = link;
                    tagfile.Tag.Track = Convert.ToUInt16(songnum);
                    tagfile.Save();

                    row.Cells["Tagged"].Value = "1";
                    row.Cells["Converted"].Value = "Yes";
                }
                else if (dlpercent == 100 && row.Cells["Tagged"].Value.ToString() == "1")
                {
                    row.Cells["Converted"].Value = "Yes";
                }
            }
        }

        private void SaveDataGridViewToCSV()
        {
            if (isSaving == true) return; //no sense saving if it's already trying
            string filePath = Application.StartupPath + "\\grid.dat";
            isSaving = true;

            try
            {
                // Create the CSV file and write the header
                using (var streamWriter = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    // Write header
                    for (int i = 0; i < dgv_downloads.Columns.Count; i++)
                    {
                        streamWriter.Write(dgv_downloads.Columns[i].HeaderText);
                        if (i < dgv_downloads.Columns.Count - 1)
                            streamWriter.Write("|");
                    }
                    streamWriter.WriteLine();

                    // Write data
                    for (int i = 0; i < dgv_downloads.Rows.Count; i++)
                    {
                        for (int j = 0; j < dgv_downloads.Columns.Count; j++)
                        {
                            streamWriter.Write(dgv_downloads.Rows[i].Cells[j].Value);
                            if (j < dgv_downloads.Columns.Count - 1)
                                streamWriter.Write("|");
                        }
                        streamWriter.WriteLine();
                    }
                }

                Console.WriteLine($"Data saved to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving data: {ex.Message}");
            }
            finally
            {
                isSaving = false;
            }
        }

        private void LoadCSVIntoDataGridView()
        {
            string filePath = Application.StartupPath + "\\grid.dat";
            dgv_downloads.Rows.Clear();

            if (!System.IO.File.Exists(filePath)) return;

            try
            {
                // Read all lines from the CSV file
                string[] lines = System.IO.File.ReadAllLines(filePath, Encoding.UTF8);

                // Add rows to the DataGridView
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] values = lines[i].Split('|');

                    // Create a new row
                    int rowIndex = dgv_downloads.Rows.Add();

                    // Set cell values for the row
                    for (int j = 0; j < values.Length; j++)
                    {
                        dgv_downloads.Rows[rowIndex].Cells[j].Value = values[j];
                    }

                    // if the DLPercent is not 100 or 0, then restart the download
                    Int16 dlpercent = Convert.ToInt16(dgv_downloads.Rows[rowIndex].Cells["DL"].Value.ToString());
                    if (dlpercent > 0 && dlpercent < 100) dgv_downloads.Rows[rowIndex].Cells["DL"].Value = "0";
                }

                dgv_downloads.Refresh();
                Console.WriteLine($"Data loaded from {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading data: {ex.Message}");
            }
        }

        private void dgv_downloads_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void timer_save_Tick(object sender, EventArgs e)
        {
            SaveDataGridViewToCSV();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (var folderBrowserDialog = new FolderBrowserDialog())
            {
                // Set the initial folder if needed
                // folderBrowserDialog.SelectedPath = "C:\\";

                // Set the title of the dialog
                folderBrowserDialog.Description = "Select a download folder";

                // Show the dialog and get the result
                DialogResult result = folderBrowserDialog.ShowDialog();

                // Check if the user clicked OK
                if (result == DialogResult.OK)
                {
                    // Get the selected folder path
                    string selectedFolder = folderBrowserDialog.SelectedPath;
                    txt_Dir.Text = selectedFolder;
                    Properties.Settings.Default.Directory = selectedFolder;
                    Properties.Settings.Default.Save();
                }
                else
                {
                    // Handle the case where the user canceled the dialog
                    Console.WriteLine("Folder selection canceled by the user.");
                }
            }

        }

        private void txt_URL_TextChanged(object sender, EventArgs e)
        {
        }

        private async void timer_convert_Tick(object sender, EventArgs e)
        {
            if (!Directory.Exists(txt_Dir.Text)) return;

            // Get subdirectories
            string[] subdirectories = Directory.GetDirectories(txt_Dir.Text);

            // Get all files with a ".webm" extension in subdirectories
            string[] nonMp3Files = Directory.EnumerateFiles(txt_Dir.Text, "*", SearchOption.AllDirectories)
                .Where(file => !file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Console.WriteLine("\nNon-mp3 files files:" + nonMp3Files.Length);

            // converts non-mp3 to mp3
            foreach (string nonMp3File in nonMp3Files)
            {
                foreach (DataGridViewRow row in dgv_downloads.Rows)
                {
                    if (row.Cells[0].Value == null || row.Cells[0].Value.ToString().Length == 0 || row.Cells["DL"].Value.ToString() != "100") continue;
                    // review levenshtein distance for mp3 file and song name so we only convert the mp3
                    string foundsong = nonMp3File.Substring(nonMp3File.LastIndexOf('\\') + 2);
                    foundsong = foundsong.Substring(foundsong.IndexOf('-') + 2, foundsong.LastIndexOf('.') - 4);
                    string cellsong = row.Cells["Song"].Value.ToString();
                    int lev = Levenshtein.Distance(foundsong, cellsong);

                    // if threshold is met, then convert non-mp3 to mp3
                    if (lev > 70)
                    {
                        await ConvertFile(nonMp3File, nonMp3File.Substring(nonMp3File.LastIndexOf('.')));
                        Console.WriteLine(nonMp3File);
                    }
                }
            }
        }

        private string GetValidFilename(string inputFilename)
        {
            // Get invalid characters in a filename
            char[] invalidChars = Path.GetInvalidFileNameChars();

            // Replace invalid characters with an underscore
            string validFilename = new string(inputFilename.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return validFilename;
        }

        private void btn_Pause_Click(object sender, EventArgs e)
        {
            isPaused = true;
            timer1.Enabled = false;
            timer_convert.Enabled = false;
            timer_tag.Enabled = false;
            btn_Pause.Visible = false;
            btn_Resume.Visible = true;
        }

        private void btn_Resume_Click(object sender, EventArgs e)
        {
            isPaused = false;
            timer1.Enabled = true;
            timer_convert.Enabled = true;
            timer_tag.Enabled = true;
            btn_Pause.Visible = true;
            btn_Resume.Visible = false;
        }

        private async void button3_Click(object sender, EventArgs e)
        {
        }

        private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isSaving != false)
            {
                e.Cancel = true;
                isPaused = true;
                timer1.Enabled = false;
                btn_Pause.Visible = false;
                btn_Resume.Visible = true;

                MessageBox.Show("Your progress is saving. I will pause progress and attempt to close again in [5] seconds.", "Close Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // Use Task.Run to avoid marking the FormClosing event handler as async
                await Task.Run(async () =>
                {
                    // Introduce a CancellationToken for cleanup
                    using (var cancellationTokenSource = new CancellationTokenSource())
                    {
                        // Allow cancellation after 5 seconds
                        await Task.Delay(5000, cancellationTokenSource.Token);

                        // Ensure the cancellation is not already requested before closing
                        if (!cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            // Close the form
                            BeginInvoke(new Action(() => Close()));
                        }
                    }
                });
            }
        }

        private void dgv_downloads_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // ensure something is actually selected
            if (e.RowIndex >= 0 && e.RowIndex < dgv_downloads.Rows.Count)
            {
                int rowIndex = e.RowIndex;

                // get the selected band name
                string bandname = dgv_downloads.Rows[rowIndex].Cells[0].Value.ToString();

                // correct the band name
                string userInput = Interaction.InputBox("If the band name is incorrect, this is your chance to update it:", "YTAD", bandname);

                // if null then ignore
                if (userInput != null && userInput.Length > 1)
                {
                    // update band name for each row with the wrong band name
                    foreach (DataGridViewRow row in dgv_downloads.Rows)
                    {
                        // no null cells!
                        if (row.Cells[0].Value != null && row.Cells[0].Value.ToString().Length > 1)
                        {
                            // if the band name is the same as the one that needs correcting then correct it
                            if (row.Cells[0].Value.ToString() == bandname)
                            {
                                row.Cells[0].Value = userInput;
                            }
                        }
                    }

                    // refresh table
                    dgv_downloads.Refresh();

                    // save
                    SaveDataGridViewToCSV();
                }
            }
        }

        private void btn_secret_MouseDoubleClick(object sender, MouseEventArgs e)
        {

        }

        private void dgv_downloads_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                contextMenuStrip1.Show(MousePosition.X, MousePosition.Y);
            }
        }

        private void menu_cleardata_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("Are you sure want to delete your data?", "YouTube Album Downloader", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (dr == DialogResult.Yes)
            {
                dgv_downloads.Rows.Clear();
                SaveDataGridViewToCSV();
            }
        }

        private void btn_secret_Click(object sender, EventArgs e)
        {

        }

        private void openDataFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("explorer.exe", Application.StartupPath);
        }

        private void RestartBadItems_Click(object sender, EventArgs e)
        {
            foreach (DataGridViewRow row in dgv_downloads.Rows)
            {
                if (row.Cells[0].Value == null || row.Cells[0].Value.ToString().Length == 0) continue;

                if (row.Cells["Converted"].Value.ToString() == "No")
                {
                    row.Cells["DL"].Value = "0";
                }
            }
        }

        private void timer_count_Tick(object sender, EventArgs e)
        {
            Int32 NotStarted = 0;
            Int32 Downloaded = 0;
            Int32 Failed = 0;
            Int32 Completed = 0;

            foreach (DataGridViewRow row in dgv_downloads.Rows)
            {
                if (row.Cells[0].Value != null && row.Cells[0].Value.ToString().Length > 1)
                {
                    try
                    {
                        if (row.Cells["DL"].Value.ToString() == "0") NotStarted++;
                        if (row.Cells["DL"].Value.ToString() == "100") Downloaded++;
                        if (row.Cells["DL"].Value.ToString() == "100" && row.Cells["Converted"].Value.ToString() == "No") Failed++;
                        if (row.Cells["DL"].Value.ToString() == "100" && row.Cells["Tagged"].Value.ToString() == "1") Completed++;
                    }
                    catch
                    {
                        break;
                    }

                    continue;
                }
            }

            lbl_status.Text = "Not Started: " + NotStarted.ToString() + "  �  Downloaded: " + Downloaded.ToString() + "  �  Failed: " + Failed.ToString() + "  �  Completed: " + Completed.ToString();
        }

        private void saveTableToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveDataGridViewToCSV();
            MessageBox.Show("Your data has been saved!", "YouTube Album Downloader", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
