using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LinkDownloaderPOC
{
    public partial class MainForm : Form
    {
        private Process _proc = null;
        private string _workingDirectory = string.Empty;
        private string _downloadFolder = string.Empty;
        private string _dupFolder = string.Empty;
        private string _lastCommandLineOutput;
        private bool _isReadyForUrlInput, _isCompleted, _errorOccured, _reprocess, _dlStarted, _loggedIn;
        private List<string> _albumLog;
        private List<string> _albumLogWithTimestamp;
        private string _tidalDownloadFolder = string.Empty;
        private string _activeAlbumId = string.Empty;
        private string _activeAlbumName = string.Empty;

        private int _albumLogStartIndex;
        private int _albumLogStopIndex;

        private ListViewItem _activeItem = null;

        private HashSet<string> _hsLinks = null;
        
        public MainForm()
        {
            InitializeComponent();

            lvToDo.Items.Clear();

            _workingDirectory = @"I:\chimera";
            _downloadFolder = @"I:\chimera\music";
            _dupFolder = @"I:\tidal-dup";
            _lastCommandLineOutput = string.Empty;
            _isReadyForUrlInput = false;
            _isCompleted = false;
            _errorOccured = false;
            _reprocess = false;
            _dlStarted = false;
            _loggedIn = false;

            _albumLog = new List<string>();
            _albumLogWithTimestamp = new List<string>();
            _tidalDownloadFolder = @"I:\chimera\music\Tidal";

            _activeItem = null;

            _hsLinks = new HashSet<string>();

            _proc = null;

            lvToDo.View = View.Details;
            lvToDo.HeaderStyle = ColumnHeaderStyle.None;
            ColumnHeader h = new ColumnHeader();
            h.Width = lvToDo.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            lvToDo.Columns.Add(h);

            lvDone.View = View.Details;
            lvDone.HeaderStyle = ColumnHeaderStyle.None;
            ColumnHeader hDone = new ColumnHeader();
            hDone.Width = lvDone.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            lvDone.Columns.Add(hDone);

            lvExceptions.View = View.Details;
            lvExceptions.HeaderStyle = ColumnHeaderStyle.None;
            ColumnHeader hExceptions = new ColumnHeader();
            hExceptions.Width = lvExceptions.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            lvExceptions.Columns.Add(hExceptions);
        }

        private void btnAddUrls_Click(object sender, EventArgs e)
        {
            using (AddURLsDialog dialog = new AddURLsDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    List<string> urls = dialog.GetURLList();

                    LogMessage(DateTime.Now.ToString() + ": " + "Adding " + urls.Count.ToString() + " items...");

                    ListViewItem item;

                    foreach (string url in urls)
                    {
                        item = new ListViewItem();
                        item.Text = url;
                        item.Name = url;
                        //lvToDo.Items.Add(item);
                        lvToDo.Items.Insert(lvToDo.Items.Count, item);
                    }
                }
            }
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            _reprocess = false;

            if (_proc != null)
                StartDownloader();
            else
                RunCommandLine();
        }

        private void RunCommandLine()
        {
            _proc = new Process();

            _proc.StartInfo.FileName = "cmd";
            _proc.StartInfo.WorkingDirectory = _workingDirectory;
            _proc.StartInfo.UseShellExecute = false;  // ShellExecute = true not allowed when output is redirected..
            _proc.StartInfo.RedirectStandardInput = true;
            _proc.StartInfo.RedirectStandardOutput = true;
            _proc.StartInfo.RedirectStandardError = true;
            _proc.StartInfo.CreateNoWindow = true;
            _proc.EnableRaisingEvents = true;
            _proc.Exited += process_Exited;
            _proc.OutputDataReceived += OutputDataReceived;
            _proc.ErrorDataReceived += ErrorDataReceived;

            LogMessage(DateTime.Now.ToString() + ": " + "Starting command prompt...");
            _proc.Start();
            _proc.BeginOutputReadLine();
            LogMessage(DateTime.Now.ToString() + ": " + "Command prompt started.");

            _proc.BeginErrorReadLine();

            StartDownloader();
        }

        private void StartDownloader()
        {
            if (_errorOccured)
            {
                LogMessage(DateTime.Now.ToString() + ": " + "StartDownloader: Error has occured so suspend processing.");
                return;
            }

            if (_dlStarted)
            {
                LogMessage(DateTime.Now.ToString() + ": " + "downloader already running.");
            }
            else
            {
                LogMessage(DateTime.Now.ToString() + ": " + "Commencing downloader...");

                string downloaderCommand = "python -u main.py";

                _proc.StandardInput.WriteLine(downloaderCommand);

                _dlStarted = true;

                LogMessage(DateTime.Now.ToString() + ": " + "downloader started.");

                downloaderCommand = "tidal";

                _proc.StandardInput.WriteLine(downloaderCommand);

                downloaderCommand = "login";

                _proc.StandardInput.WriteLine(downloaderCommand);
            }

            if (lvToDo.Items.Count <= 0)
            {
                LogMessage(DateTime.Now.ToString() + ": " + "No items to process.");
                return;
            }
            else
            {
                ProcessAlbum();
            }
        }

        private void ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            // chimera writes download progress to the error stream !!!???

            if (null != e.Data)
            {
                string output = string.Empty;

                _lastCommandLineOutput = e.Data.ToString().Trim();

                if (_lastCommandLineOutput. StartsWith("downloading: ") || _lastCommandLineOutput.StartsWith("Download count: ") || _lastCommandLineOutput.StartsWith("grabbing ") || (_lastCommandLineOutput.Equals("")))
                {
                    // consider as standard output
                    output = DateTime.Now.ToString() + ": " + _lastCommandLineOutput;
                    _errorOccured = false;

                    // build album log
                    //if (_lastCommandLineOutput.Contains("downloading:") && _lastCommandLineOutput.Contains("%|"))
                    //{
                    //    //do not add to album log
                    //}
                    //else
                    //{
                    //    _albumLog.Add(_lastCommandLineOutput);

                    //    _albumLogWithTimestamp.Add(output);

                    //    if (_lastCommandLineOutput.StartsWith("grabbing tidal album "))
                    //    {
                    //        _activeAlbumName = _lastCommandLineOutput.Substring(21);
                    //    }
                    //}
                }
                else
                {
                    output = DateTime.Now.ToString() + ": [Error] " + _lastCommandLineOutput;
                    _errorOccured = true;

                    return;
                }

                if (_lastCommandLineOutput.Equals("TIDAL ->"))
                {
                    _isReadyForUrlInput = true;
                }
                else
                {
                    _isReadyForUrlInput = false;
                }

                if (_lastCommandLineOutput.Equals("TIDAL ->"))
                {
                    _isCompleted = true;
                }
                else
                {
                    _isCompleted = false;
                }

                // Must run the update of the listbox in the same thread that created it..
                lstbxOutput.Invoke(
                    new OutputDataToListboxDelegate(OutputDataToListbox),
                    output
                );
            }
        }

        private delegate void OutputDataToListViewBoxDelegate(String s);

        private void process_Exited(object sender, EventArgs e)
        {
            lstbxLog.Invoke(
                    new OutputDataToListViewBoxDelegate(OutputDataToListViewbox),
                    DateTime.Now.ToString() + ": " + "Command Line Process has shut down.");
        }

        private void OutputDataToListViewbox(String s)
        {
            LogMessage(s);
        }

        private delegate void OutputDataToListboxDelegate(String s);

        private void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (null != e.Data)
            {
                _lastCommandLineOutput = e.Data.ToString().Trim();

                string output = string.Empty;

                if (_lastCommandLineOutput.Contains("logged into Tidal"))
                {
                    _loggedIn = true;

                    ProcessAlbum();
                }
                
                //if (_lastCommandLineOutput.Equals("TIDAL ->"))
                //{
                //    _isReadyForUrlInput = true;
                //}
                //else
                //{
                //    _isReadyForUrlInput = false;
                //}

                //if (_lastCommandLineOutput.Equals("TIDAL ->"))
                //{
                //    _isCompleted = true;
                //}
                //else
                //{
                //    _isCompleted = false;
                //}

                bool test = false;

                if (_lastCommandLineOutput.Equals("TIDAL -> unknown command..."))
                {
                    // likely the current album has completed processing and awaiting input for the next one
                    _isCompleted = true;
                    _isReadyForUrlInput = true;
                    test = true;
                   
                }
                else
                {
                    _isReadyForUrlInput = false;
                    _isCompleted = false;
                }

                output = DateTime.Now.ToString() + ": " + _lastCommandLineOutput;

                // build album log
                //if (_lastCommandLineOutput.Contains("downloading:") && _lastCommandLineOutput.Contains("%|"))
                //{
                //    //do not add to album log
                //}
                //else
                //{
                //    _albumLog.Add(_lastCommandLineOutput);
                    
                //    _albumLogWithTimestamp.Add(output);
                    
                //    if (_lastCommandLineOutput.StartsWith("grabbing tidal album "))
                //    {
                //        _activeAlbumName = _lastCommandLineOutput.Substring(21);
                //    }
                //}

                if (test)
                {
                    output = DateTime.Now.ToString() + ": ready for input from output";
                    test = false;
                }

                // Must run the update of the listbox in the same thread that created it..
                lstbxOutput.Invoke(
                    new OutputDataToListboxDelegate(OutputDataToListbox),
                    output
                );
            }
        }

        private void OutputDataToListbox(String s)
        {
            lstbxOutput.Items.Add(s);
            lstbxOutput.TopIndex = lstbxOutput.Items.Count - 1;

            // the flags set in the output data event handler are used here
            // the processing is not done in the event handler itself in order to get the listbox updated with the cmd output as quickly as possible

            if (_isCompleted)
            {
                _albumLogStopIndex = lstbxOutput.Items.Count - 1;

                _albumLog.Clear();
                _albumLogWithTimestamp.Clear();

                int indexTimestamp;
                string logText = string.Empty;

                // extract the album log
                for (int k = _albumLogStartIndex; k <= _albumLogStopIndex; k++)
                {
                    if (lstbxOutput.Items[k].ToString().Contains("downloading:") && lstbxOutput.Items[k].ToString().Contains("%|"))
                    {
                        //do not add download progress
                    }
                    else
                    {
                        indexTimestamp = lstbxOutput.Items[k].ToString().IndexOf("M: ");
                        logText = lstbxOutput.Items[k].ToString().Substring(indexTimestamp + 3);

                        _albumLog.Add(logText);
                        _albumLogWithTimestamp.Add(lstbxOutput.Items[k].ToString());
                    }
                }

                var info = new DirectoryInfo(_tidalDownloadFolder);
                var latestDirectory = info.GetDirectories("*", SearchOption.AllDirectories)
                                          .OrderByDescending(d => d.CreationTime)
                                          .FirstOrDefault();

                // analyze the album log
                int logEntryCount = _albumLog.Count;
                
                string matchText = "Download count: ";
                string matchTextDownload = "downloading: ";
                string matchTextGrabbing = "grabbing ";

                bool folderFound = false;

                bool repeat = false;
                bool fileMatchFound = false;

                for (int i = 3; i < logEntryCount; i++)
                {
                    fileMatchFound = false;

                    if (!folderFound)
                    {
                        if (_albumLog[i].StartsWith(matchTextGrabbing))
                        {
                            string trackText = _albumLog[i].Substring(matchTextGrabbing.Length);
                            int index = _albumLog[i].IndexOf(" by");
                            string trackName = _albumLog[i].Substring(matchTextGrabbing.Length, index - matchTextGrabbing.Length);

                            string sanitizedTrackName = removeInvalidChars.Replace(trackName, string.Empty);
                            sanitizedTrackName = NormalizeSpacesWithRegex(sanitizedTrackName);

                            var matchedFiles = latestDirectory.GetFiles("*" + sanitizedTrackName + ".flac");

                            if (matchedFiles.Length == 1)
                            {
                                LogMessage(DateTime.Now.ToString() + ": " + "One file match found for the track: " + trackName + " in the file '" + matchedFiles[0] + "' and so the folder can be considered as verified");

                                folderFound = true;
                            }
                        }
                    }

                    if (_albumLog[i].StartsWith(matchText))
                    {
                        // check if the previous entry says downloading
                        if (_albumLog[i - 1].StartsWith(matchTextDownload))
                        {
                            // the track got downloaded completely
                            LogMessage(DateTime.Now.ToString() + ": " + "Validated that the track at download count " + _albumLog[i].Substring(matchText.Length) + " has been downloaded completely.");

                            continue;
                        }
                        else
                        {
                            // the track was likely not downloaded completely
                            LogMessage(DateTime.Now.ToString() + ": " + "The track at download count " + _albumLog[i].Substring(matchText.Length) + " does not seem to have been downloaded completely.");

                            if (!repeat)
                                repeat = true;

                            // find the corresponding grabbing entry
                            if (_albumLog[i - 2].StartsWith(matchTextGrabbing))
                            {
                                string trackText = _albumLog[i - 2].Substring(matchTextGrabbing.Length);
                                int index = _albumLog[i - 2].IndexOf(" by");
                                string trackName = _albumLog[i - 2].Substring(matchTextGrabbing.Length, index - matchTextGrabbing.Length);

                                LogMessage(DateTime.Now.ToString() + ": " + trackText + " was likely not downloaded completely.");

                                string sanitizedTrackName = removeInvalidChars.Replace(trackName, string.Empty);
                                sanitizedTrackName = NormalizeSpacesWithRegex(sanitizedTrackName);

                                // search the download directory for the file

                                var matchedFiles = latestDirectory.GetFiles("*"+ sanitizedTrackName + ".flac");

                                if (matchedFiles.Length > 0)
                                {
                                    fileMatchFound = true;

                                    if (matchedFiles.Length > 1)
                                    {
                                        // more than one matching file found

                                        LogMessage(DateTime.Now.ToString() + ": " + "More than one file match found for the track: " + trackName + ".");

                                        MessageBox.Show("More than one file match found for the track: " + trackName + ".", "File match found", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                    }
                                    else
                                    {
                                        // match found so delete the incomplete file
                                        LogMessage(DateTime.Now.ToString() + ": " + "One file match found for the track: " + trackName + " in the file '" + matchedFiles[0] + "'.");

                                        DialogResult msgboxResult = MessageBox.Show("One file match found for the track: " + trackName + " in the file '" + matchedFiles[0] + "'. Do you want to delete this file?", "Delete file confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                                        if (msgboxResult == DialogResult.Yes)
                                        {
                                            matchedFiles[0].Delete();

                                            LogMessage(DateTime.Now.ToString() + ": " + "File '" + matchedFiles[0] + "' has been deleted.");
                                        }

                                        if (!folderFound)
                                            folderFound = true;
                                    }
                                }
                                else
                                {
                                    // no file match was found
                                    fileMatchFound = false;
                                }

                                if (!fileMatchFound)
                                {
                                    LogMessage(DateTime.Now.ToString() + ": " + "A file match could not be found for the track: " + trackName + ".");

                                    MessageBox.Show("A file match could not be found for the track: " + trackName + ".", "Matching file not found", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                }
                            }
                            else
                            {
                                // the corresponding grabbing text could not be found
                                LogMessage(DateTime.Now.ToString() + ": " + "A text match for the album could not be found for the log entry: " + _albumLog[i] + ".");

                                MessageBox.Show("A text match for the album could not be found for the log entry: " + _albumLog[i] + ".", "Matching text not found", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                            }
                        }
                    }
                }

                if (repeat)
                {
                    DialogResult msgboxResult = MessageBox.Show("Repeat processing indicated for '" + _activeItem.Text + "'. Do you want to repeat processing?", "Repeat processing confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (msgboxResult == DialogResult.Yes)
                    {
                        _hsLinks.Remove(_activeItem.Text);

                        repeat = true;
                    }
                    else
                    {
                        repeat = false;
                    }
                }

                // save the log in the destination folder

                if (folderFound)
                {
                    string albumLogFileName = "log_" + _activeAlbumId + ".txt";
                    string albumLogFilePath = latestDirectory.FullName + "\\" + albumLogFileName;
                    //File.AppendAllLines(albumLogFilePath, _albumLog);
                    File.AppendAllLines(albumLogFilePath, _albumLogWithTimestamp);

                    LogMessage(DateTime.Now.ToString() + ": " + "Album log file '" + albumLogFileName + "' has been written out at '" + latestDirectory + "'.");
                }

                if (!repeat)
                {
                    // move the current item to the completed list

                    tbxCurrentURL.Clear();

                    ListViewItem itemToAdd = new ListViewItem(_activeItem.Text);
                    itemToAdd.Name = _activeItem.Name;
                    itemToAdd.Text = _activeItem.Text;
                    lvDone.Items.Add(itemToAdd);
                    lvToDo.Items.Remove(_activeItem);

                    // start up the download for the next item

                    _isCompleted = false;
                    _albumLog.Clear();
                    _albumLogWithTimestamp.Clear();
                    _activeAlbumId = string.Empty;
                    _activeAlbumName = string.Empty;

                    if (lvToDo.Items.Count <= 0)
                    {
                        LogMessage(DateTime.Now.ToString() + ": " + "No items to process.");
                    }
                    //else
                    //{
                    //    StartDownloader();
                    //}
                }
                else
                {

                }
                
               
            }

            if (_isReadyForUrlInput)
            {
                _albumLogStartIndex = lstbxOutput.Items.Count - 1;

                ProcessAlbum();
            }

            if (_errorOccured)
            {
                // move the current item to the error list

                ListViewItem itemToAdd = new ListViewItem(_activeItem.Text);
                itemToAdd.Name = _activeItem.Name;
                itemToAdd.Text = _activeItem.Text;
                lvExceptions.Items.Add(itemToAdd);
                lvToDo.Items.Remove(_activeItem);

                return;

                //_errorOccured = false;
            }
        }

        private static readonly Regex removeInvalidChars = new Regex($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]",
            RegexOptions.None | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex MultipleSpaces = new Regex(@" {2,}", RegexOptions.Compiled);

        static string NormalizeSpacesWithRegex(string input)
        {
            return MultipleSpaces.Replace(input, " ");
        }

        private void ProcessAlbum()
        {
            tbxCurrentURL.Clear();

            if (lstbxOutput.Items.Count > 30000)
            {
                SaveOutput();
                lstbxOutput.Items.Clear();
            }

            if (!_reprocess)
            {
                if (lvToDo.Items.Count <= 0)
                {
                    LogMessage(DateTime.Now.ToString() + ": " + "No items to process.");
                    return;
                }
                else
                {
                    _activeItem = lvToDo.Items[0];
                }
            }
            else
            {
                if (lvExceptions.Items.Count <= 0)
                {
                    LogMessage(DateTime.Now.ToString() + ": " + "No items to process.");
                    _reprocess = false;
                    return;
                }
                else
                {
                    _activeItem = lvExceptions.Items[0];
                }
            }

            if (null == _activeItem)
            {
                LogMessage(DateTime.Now.ToString() + ": " + "No items to process.");
                return;
            }

            string albumUrl = _activeItem.Text;
            string albumId = new Uri(albumUrl).Segments.Last();

            tbxCurrentURL.Text = albumUrl;

            // clear flags
            _isReadyForUrlInput = false;

            if (_hsLinks.Contains(albumUrl))
            {
                LogMessage(DateTime.Now.ToString() + ": " + "Skipping album '" + albumUrl + "' as it appears to have been processed in this session.");
            }
            else
            {
                _hsLinks.Add(albumUrl);

                _activeAlbumId = albumId;
                
                _proc.StandardInput.WriteLine("grab album " + albumId);

                _proc.StandardInput.WriteLine(" ");
            }
        }

        private void btnProcessExceptions_Click(object sender, EventArgs e)
        {
            _reprocess = true;

            RunCommandLine();
        }

        private void btnClearExceptions_Click(object sender, EventArgs e)
        {
            lvExceptions.Items.Clear();
        }

        private void btnClearToDo_Click(object sender, EventArgs e)
        {
            lvToDo.Items.Clear();
        }

        private void btnWriteToDo_Click(object sender, EventArgs e)
        {
            StringBuilder sb = new StringBuilder();

            foreach (ListViewItem item in lvToDo.Items)
            {
                sb.Append(item.Text);
                sb.Append(Environment.NewLine);
            }

            DateTime dt = DateTime.Now;
            string formattedDateTime = String.Format("{0:yyyy-MM-dd_HH-mm-ss}", dt);

            string outputFileName = "todo_" + formattedDateTime + ".txt";

            string folderPath = Directory.GetCurrentDirectory();

            string filePath = folderPath + "\\" + outputFileName;

            File.WriteAllText((filePath), sb.ToString());

            LogMessage(DateTime.Now.ToString() + ": " + "To Do file '" + outputFileName + "' has been written out at '" + folderPath + "'.");
        }

        private void btnClearDone_Click(object sender, EventArgs e)
        {
            lvDone.Items.Clear();
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            _proc.StandardInput.WriteLine("exit");

            _proc = null;
        }

        private void btnClearOutput_Click(object sender, EventArgs e)
        {
            lstbxOutput.Items.Clear();
        }

        private void btnWriteDone_Click(object sender, EventArgs e)
        {
            SaveCompleted();
        }

        private void SaveCompleted()
        {
            StringBuilder sb = new StringBuilder();

            foreach (ListViewItem item in lvDone.Items)
            {
                sb.Append(item.Text);
                sb.Append(Environment.NewLine);
            }

            DateTime dt = DateTime.Now;
            string formattedDateTime = String.Format("{0:yyyy-MM-dd_HH-mm-ss}", dt);

            string outputFileName = "done_" + formattedDateTime + ".txt";

            string folderPath = Directory.GetCurrentDirectory();

            string filePath = folderPath + "\\" + outputFileName;

            File.WriteAllText((filePath), sb.ToString());

            LogMessage(DateTime.Now.ToString() + ": " + "Done file '" + outputFileName + "' has been written out at '" + folderPath + "'.");
        }

        private void btnSaveClearOutput_Click(object sender, EventArgs e)
        {
            SaveOutput();
            lstbxOutput.Items.Clear();
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            if (_proc != null)
                _proc = null;

            _lastCommandLineOutput = string.Empty;
            _isReadyForUrlInput = false;
            _isCompleted = false;
            _errorOccured = false;
            _reprocess = false;
            _dlStarted = false;
            _loggedIn = false;

            _activeItem = null;
        }

        private void btnWriteOutput_Click(object sender, EventArgs e)
        {
            SaveOutput();
        }

        private void SaveOutput()
        {
            StringBuilder sb = new StringBuilder();

            foreach (string item in lstbxOutput.Items)
            {
                if (item.Contains("downloading:") && item.Contains("%|"))
                {
                    //do not add download progress
                }
                else
                {
                    sb.Append(item);
                    sb.Append(Environment.NewLine);
                }
            }

            DateTime dt = DateTime.Now;
            string formattedDateTime = String.Format("{0:yyyy-MM-dd_HH-mm-ss}", dt);

            string outputFileName = "output_" + formattedDateTime + ".txt";

            string folderPath = Directory.GetCurrentDirectory();

            string filePath = folderPath + "\\" + outputFileName;

            File.WriteAllText((filePath), sb.ToString());

            LogMessage(DateTime.Now.ToString() + ": " + "Output file '" + outputFileName + "' has been written out at '" + folderPath + "'.");
        }

        private void btnWriteExceptionFile_Click(object sender, EventArgs e)
        {
            StringBuilder sb = new StringBuilder();

            foreach (ListViewItem item in lvExceptions.Items)
            {
                sb.Append(item.Text);
                sb.Append(Environment.NewLine);
            }

            DateTime dt = DateTime.Now;
            string formattedDateTime = String.Format("{0:yyyy-MM-dd_HH-mm-ss}", dt);

            string outputFileName = "exception_" + formattedDateTime + ".txt";

            string folderPath = Directory.GetCurrentDirectory();
            
            string filePath = folderPath + "\\" + outputFileName;

            File.WriteAllText((filePath), sb.ToString());

            LogMessage(DateTime.Now.ToString() + ": " + "Exception file '" + outputFileName + "' has been written out at '" + folderPath + "'.");
        }
 
        private void btnTest_Click(object sender, EventArgs e)
        {
            string albumPath = _downloadFolder + "\\" + "test";

            if (Directory.Exists(albumPath))
            {
                if (!Directory.Exists(_dupFolder))
                {
                    Directory.CreateDirectory(_dupFolder);
                }

                Directory.Move(albumPath, _dupFolder + "\\" + "test");
            }
        }

        private void LogMessage(string message)
        {
            lstbxLog.Items.Add(message);
            lstbxLog.TopIndex = lstbxLog.Items.Count - 1;
        }
    }
}
