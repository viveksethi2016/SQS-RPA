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
        private bool _isReadyForUrlInput, _isCompleted, _errorOccured, _reprocess, _dlStarted;
        private List<string> _albumLogWithTimestamp;
        
        private int _albumLogStartIndex;
        private int _albumLogStopIndex;

        private ListViewItem _activeItem = null;

        private HashSet<string> _hsLinks = null;
        
        public MainForm()
        {
            InitializeComponent();

            lvToDo.Items.Clear();

            _workingDirectory = @"H:";
            _downloadFolder = @"H:\qobuz";
            _dupFolder = @"H:\qobuz-dup";
            _lastCommandLineOutput = string.Empty;
            _isReadyForUrlInput = false;
            _isCompleted = false;
            _errorOccured = false;
            _reprocess = false;
            _dlStarted = false;

            _albumLogWithTimestamp = new List<string>();
            
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

                string downloaderCommand = "python -u Qo-DL.py";

                _proc.StandardInput.WriteLine(downloaderCommand);

                _dlStarted = true;

                LogMessage(DateTime.Now.ToString() + ": " + "downloader started.");
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
            if (null != e.Data)
            {
                _lastCommandLineOutput = e.Data.ToString().Trim();

                string output = DateTime.Now.ToString() + ": [Error] " + _lastCommandLineOutput;

                _errorOccured = true;

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

                if (_lastCommandLineOutput.Equals("Input Qobuz Player or Qobuz store URL:"))
                {
                    _isReadyForUrlInput = true;
                }
                else
                {
                    _isReadyForUrlInput = false;
                }

                if (_lastCommandLineOutput.Equals("Returning to URL input screen..."))
                {
                    _isCompleted = true;
                }
                else
                {
                    _isCompleted = false;
                }

                string output = DateTime.Now.ToString() + ": " + _lastCommandLineOutput;

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

                _albumLogWithTimestamp.Clear();

                string logText = string.Empty;

                // extract the album log
                for (int k = _albumLogStartIndex; k <= _albumLogStopIndex; k++)
                {
                    if (lstbxOutput.Items[k].ToString().Contains("[*]"))
                    {
                        string[] pieces = lstbxOutput.Items[k].ToString().Split(new[] { "[*]" }, StringSplitOptions.None);
                        _albumLogWithTimestamp.Add(pieces[pieces.Length - 1]);
                    }
                    else
                    {
                        _albumLogWithTimestamp.Add(lstbxOutput.Items[k].ToString());
                    }
                }

                var info = new DirectoryInfo(_downloadFolder);
                var latestDirectory = info.GetDirectories("*", SearchOption.TopDirectoryOnly)
                                          .OrderByDescending(d => d.CreationTime)
                                          .FirstOrDefault();

                Uri uri = new Uri(_activeItem.Text);
                
                string albumLogFileName = "log_" + uri.Segments[uri.Segments.Length - 1] + ".txt";
                string albumLogFilePath = latestDirectory.FullName + "\\" + albumLogFileName;
                //File.AppendAllLines(albumLogFilePath, _albumLog);
                File.AppendAllLines(albumLogFilePath, _albumLogWithTimestamp);

                LogMessage(DateTime.Now.ToString() + ": " + "Album log file '" + albumLogFileName + "' has been written out at '" + latestDirectory + "'.");

                // move the current item to the completed list

                tbxCurrentURL.Clear();
                
                ListViewItem itemToAdd = new ListViewItem(_activeItem.Text);
                itemToAdd.Name = _activeItem.Name;
                itemToAdd.Text = _activeItem.Text;
                lvDone.Items.Add(itemToAdd);
                lvToDo.Items.Remove(_activeItem);

                // start up the download for the next item

                _isCompleted = false;

                if (lvToDo.Items.Count <= 0)
                {
                    LogMessage(DateTime.Now.ToString() + ": " + "No items to process.");
                }
                else
                {
                    StartDownloader();
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

        private void ProcessAlbum()
        {
            tbxCurrentURL.Clear();

            if (_errorOccured)
            {
                LogMessage(DateTime.Now.ToString() + ": " + "ProcessAlbum: Error has occured so suspend processing.");
                return;
            }

            if (lvToDo.Items.Count > 30000)
            {
                SaveOutput();
                lstbxOutput.Items.Clear();

                SaveCompleted();
                lvDone.Items.Clear();
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

                _proc.StandardInput.WriteLine(albumUrl);
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

        private void btnClearOutput_Click(object sender, EventArgs e)
        {
            lstbxOutput.Items.Clear();
        }

        private void btnWriteOutput_Click(object sender, EventArgs e)
        {
            SaveOutput();
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

            _dlStarted = false;
        }

        private void btnExitCmd_Click(object sender, EventArgs e)
        {
            _proc.Kill();
        }

        private void SaveOutput()
        {
            StringBuilder sb = new StringBuilder();

            foreach (string item in lstbxOutput.Items)
            {
                if (item.Contains("[*]"))
                {
                    string[] pieces = item.Split(new[] { "[*]" }, StringSplitOptions.None);

                    sb.Append(pieces[pieces.Length - 1]);
                    sb.Append(Environment.NewLine);
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
