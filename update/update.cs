using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.0")]
[assembly: ComVisible(false)]

[assembly: AssemblyTitle("update")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Artur Kurpukov")]
[assembly: AssemblyProduct("update")]
[assembly: AssemblyCopyright("Copyright (C) 2019-2023 Artur Kurpukov")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyFileVersion("1.1.0")]

class update:Form {
    private readonly IContainer _components = new Container();
    
    private readonly SerialPort _serialPort1;
    
    private string _fileName;
    
    private UInt16 CRC16(byte ch, UInt16 oldCRC) {
        uint m = (((uint)oldCRC << 8) | ch);
        for (int n = 0; n < 8; n++) {
            m <<= 1;
            if ((m & 0x1000000) != 0) {
                m ^= 0x800500;
            }
        }
        return (UInt16)(m >> 8);
    }
    
    private void Form1DragEnter(object sender, DragEventArgs e) {
        if (backgroundWorker1.IsBusy) {
            return;
        }
        
        string[] fileNames = (string[])e.Data.GetData(DataFormats.FileDrop, false);
        if (fileNames.Length == 1) {
            e.Effect = DragDropEffects.All;
        } else {
            e.Effect = DragDropEffects.None;
        }
    }
    
    private void Form1DragDrop(object sender, DragEventArgs e) {
        if (backgroundWorker1.IsBusy) {
            return;
        }
        
        string[] fileNames = (string[])e.Data.GetData(DataFormats.FileDrop, false);
        if (fileNames.Length != 1) {
            return;
        }
        
        textBox1.Text = _fileName = fileNames[0];
        comboBox2.Enabled = true;
        comboBox3.Enabled = true;
        progressBar1.Enabled = true;
        button2.Enabled = true;
    }
    
    private void Button1Click(object sender, EventArgs e) {
        openFileDialog1.FileName = null;
        if (openFileDialog1.ShowDialog(this) != DialogResult.OK) {
            return;
        }
        
        textBox1.Text = _fileName = openFileDialog1.FileName;
        comboBox2.Enabled = true;
        comboBox3.Enabled = true;
        progressBar1.Enabled = true;
        button2.Enabled = true;
    }
    
    private void ComboBox2DropDown(object sender, EventArgs e) {
        ((ComboBox)sender).Items.Clear();
        ((ComboBox)sender).Items.AddRange(SerialPort.GetPortNames());
    }
    
    private void Button2Click(object sender, EventArgs e) {
        button1.Enabled = false;
        comboBox2.Enabled = false;
        comboBox3.Enabled = false;
        button2.Enabled = false;
        
        backgroundWorker1.RunWorkerAsync();
    }
    
    private void BackgroundWorker1ProgressChanged(object sender, ProgressChangedEventArgs e) {
        progressBar1.Value = e.ProgressPercentage;
    }
    
    private void BackgroundWorker1DoWork(object sender, DoWorkEventArgs e) {
        _serialPort1.PortName = comboBox2.Text;
        try {
            _serialPort1.BaudRate = Int32.Parse(comboBox3.Text);
        } catch {
            throw new Exception("Baud rate is invalid.");
        }
        
        byte[] buf = new byte[262149];
        int size;
        
        FileStream inFile;
        try {
            inFile = new FileStream(_fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        } catch {
            throw new Exception("Couldn't find '" + _fileName + "'.");
        }
        
        try {
            size = inFile.Read(buf, 0, 262149);
        } finally {
            inFile.Close();
        }
        
        if (size > 262148) {
            throw new Exception("'" + _fileName + "' is too big.");
        }
        
        if (((size-4) % 16) != 0) {
            throw new Exception("'" + _fileName + "' is not a valid firmware file.");
        }
        
        UInt32 crc32 = 0xFFFFFFFF;
        for (int i = 0; i < size; i++) {
            crc32 ^= buf[i];
            for (int n = 0; n < 8; n++) {
                if ((crc32 & 0x00000001) != 0) {
                    crc32 = ((crc32 >> 1) ^ 0xEDB88320);
                } else {
                    crc32 >>= 1;
                }
            }
        }
        
        if (crc32 != 0) {
            throw new Exception("'" + _fileName + "' is damaged.");
        }
        
        size -= 4;
        
        _serialPort1.Open();
        try {
            const byte Error_INCOMPATIBLE = 0x88;
            const byte Error_OK = 0x11;
            const byte Error_CRC = 0x22;
            
            for (int index = 0, retries = 0; index < size;) {
                int frameSize = 0;
                
                UInt16 crc = 0x0000;
                do {
                    crc = CRC16(buf[index++], crc);
                } while (++frameSize < 16 || index < 48);
                
                crc = CRC16(0, crc);
                crc = CRC16(0, crc);
                
                _serialPort1.Write(new byte[] { (byte)(frameSize+2), }, 0, 1);
                _serialPort1.Write(buf, (index-frameSize), frameSize);
                _serialPort1.Write(new byte[] { (byte)(crc >> 8), (byte)crc, }, 0, 2);
                
                byte resp = (byte)_serialPort1.ReadByte();
                if (resp == Error_OK) {
                    retries = 0;
                } else {
                    if (frameSize == 48) {
                        if (resp == Error_INCOMPATIBLE) {
                            throw new WarningException("This firmware is for different device!");
                        }
                    }
                    if (++retries >= 3) {
                        throw new WarningException("CRC error during data transfer!");
                    }
                    index -= frameSize;
                }
                
                ((BackgroundWorker)sender).ReportProgress(1000 * index / size);
            }
        } catch (TimeoutException) {
            throw new WarningException("Target is not responding!");
        } finally {
            _serialPort1.Close();
        }
    }
    
    private void BackgroundWorker1RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) {
        Exception err = e.Error;
        if (err != null) {
            MessageBox.Show(this, err.Message, "update", MessageBoxButtons.OK, (err is WarningException ? MessageBoxIcon.Warning : MessageBoxIcon.Error));
        } else {
            MessageBox.Show(this, "Target updated successfully!", "update", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        
        button1.Enabled = true;
        comboBox2.Enabled = true;
        comboBox3.Enabled = true;
        progressBar1.Value = 0;
        button2.Enabled = true;
    }
    
    [STAThread]
    internal static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new update());
    }
    
    protected override void Dispose(bool disposing) {
        if (disposing) {
            _components.Dispose();
        }
        base.Dispose(disposing);
    }
    
    public update() {
        this.Font = new Font("Segoe UI", 9F);
        
        tableLayoutPanel1 = new TableLayoutPanel();
        
        tableLayoutPanel2 = new TableLayoutPanel();
        label1 = new Label();
        textBox1 = new TextBox();
        button1 = new Button();
        
        tableLayoutPanel4 = new TableLayoutPanel();
        label3 = new Label();
        comboBox2 = new ComboBox();
        label4 = new Label();
        comboBox3 = new ComboBox();
        
        tableLayoutPanel5 = new TableLayoutPanel();
        label5 = new Label();
        progressBar1 = new ProgressBar();
        button2 = new Button();
        
        openFileDialog1 = new OpenFileDialog();
        openFileDialog1.Title = "update";
        openFileDialog1.ReadOnlyChecked = true;
        openFileDialog1.Filter = "DAT files (*.dat)|*.dat|All files (*.*)|*.*";
        
        backgroundWorker1 = new BackgroundWorker();
        backgroundWorker1.WorkerReportsProgress = true;
        backgroundWorker1.DoWork += BackgroundWorker1DoWork;
        backgroundWorker1.ProgressChanged += BackgroundWorker1ProgressChanged;
        backgroundWorker1.RunWorkerCompleted += BackgroundWorker1RunWorkerCompleted;
        
        _serialPort1 = new SerialPort(_components);
        _serialPort1.ReadTimeout = 3000;
        
        tableLayoutPanel2.SuspendLayout();
        tableLayoutPanel4.SuspendLayout();
        tableLayoutPanel5.SuspendLayout();
        tableLayoutPanel1.SuspendLayout();
        this.SuspendLayout();
        
        tableLayoutPanel1.AutoSize = true;
        tableLayoutPanel1.ColumnCount = 1;
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 0, 0);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel4, 0, 1);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel5, 0, 2);
        tableLayoutPanel1.Dock = DockStyle.Fill;
        tableLayoutPanel1.RowCount = 3;
        tableLayoutPanel1.RowStyles.Add(new RowStyle());
        tableLayoutPanel1.RowStyles.Add(new RowStyle());
        tableLayoutPanel1.RowStyles.Add(new RowStyle());
        tableLayoutPanel1.TabIndex = 0;
        
        tableLayoutPanel2.AutoSize = true;
        tableLayoutPanel2.ColumnCount = 3;
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65F));
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel2.Controls.Add(label1, 0, 0);
        tableLayoutPanel2.Controls.Add(textBox1, 1, 0);
        tableLayoutPanel2.Controls.Add(button1, 2, 0);
        tableLayoutPanel2.Dock = DockStyle.Fill;
        tableLayoutPanel2.Margin = new Padding(6, 6, 6, 0);
        tableLayoutPanel2.RowCount = 1;
        tableLayoutPanel2.RowStyles.Add(new RowStyle());
        tableLayoutPanel2.TabIndex = 0;
        
        label1.AutoSize = true;
        label1.Dock = DockStyle.Fill;
        label1.TabIndex = 0;
        label1.Text = "&Firmware:";
        label1.TextAlign = ContentAlignment.MiddleLeft;
        
        textBox1.Dock = DockStyle.Fill;
        textBox1.ReadOnly = true;
        textBox1.TabIndex = 1;
        textBox1.Text = "";
        
        button1.AutoSize = true;
        button1.Dock = DockStyle.Fill;
        button1.Enabled = true;
        button1.Size = new Size();
        button1.TabIndex = 2;
        button1.Text = "...";
        button1.UseVisualStyleBackColor = true;
        button1.Click += Button1Click;
        
        tableLayoutPanel4.AutoSize = true;
        tableLayoutPanel4.ColumnCount = 6;
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel4.Controls.Add(label3, 0, 0);
        tableLayoutPanel4.Controls.Add(comboBox2, 1, 0);
        tableLayoutPanel4.Controls.Add(label4, 3, 0);
        tableLayoutPanel4.Controls.Add(comboBox3, 4, 0);
        tableLayoutPanel4.Dock = DockStyle.Fill;
        tableLayoutPanel4.Margin = new Padding(6, 0, 6, 0);
        tableLayoutPanel4.RowCount = 1;
        tableLayoutPanel4.RowStyles.Add(new RowStyle());
        tableLayoutPanel4.TabIndex = 1;
        
        label3.AutoSize = true;
        label3.Dock = DockStyle.Fill;
        label3.TabIndex = 0;
        label3.Text = "COM &port:";
        label3.TextAlign = ContentAlignment.MiddleLeft;
        
        comboBox2.Dock = DockStyle.Fill;
        comboBox2.Enabled = false;
        comboBox2.TabIndex = 1;
        comboBox2.Text = "COM1";
        comboBox2.DropDown += ComboBox2DropDown;
        
        label4.AutoSize = true;
        label4.Dock = DockStyle.Fill;
        label4.TabIndex = 2;
        label4.Text = "&Baud rate:";
        label4.TextAlign = ContentAlignment.MiddleLeft;
        
        comboBox3.Dock = DockStyle.Fill;
        comboBox3.Enabled = false;
        comboBox3.Items.AddRange(new object[] { 57600, 115200, 230400, });
        comboBox3.TabIndex = 3;
        comboBox3.Text = "230400";
        
        tableLayoutPanel5.AutoSize = true;
        tableLayoutPanel5.ColumnCount = 3;
        tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65F));
        tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel5.Controls.Add(label5, 0, 0);
        tableLayoutPanel5.Controls.Add(progressBar1, 1, 0);
        tableLayoutPanel5.Controls.Add(button2, 2, 0);
        tableLayoutPanel5.Dock = DockStyle.Fill;
        tableLayoutPanel5.Margin = new Padding(6, 0, 6, 6);
        tableLayoutPanel5.RowCount = 1;
        tableLayoutPanel5.RowStyles.Add(new RowStyle());
        tableLayoutPanel5.TabIndex = 2;
        
        label5.AutoSize = true;
        label5.Dock = DockStyle.Fill;
        label5.TabIndex = 0;
        label5.Text = "Progress:";
        label5.TextAlign = ContentAlignment.MiddleLeft;
        
        progressBar1.Dock = DockStyle.Fill;
        progressBar1.Enabled = false;
        progressBar1.Maximum = 1000;
        progressBar1.Style = ProgressBarStyle.Continuous;
        progressBar1.TabIndex = 1;
        
        button2.AutoSize = true;
        button2.Dock = DockStyle.Fill;
        button2.Enabled = false;
        button2.Size = new Size();
        button2.TabIndex = 2;
        button2.Text = "&Upload";
        button2.UseVisualStyleBackColor = true;
        button2.Click += Button2Click;
        
        this.AcceptButton = button2;
        this.AllowDrop = true;
        this.AutoScaleDimensions = new SizeF(6F, 13F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.AutoSize = true;
        this.ClientSize = new Size(325, -1);
        this.Controls.Add(tableLayoutPanel1);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Text = "update";
        this.DragDrop += Form1DragDrop;
        this.DragEnter += Form1DragEnter;
        
        this.ResumeLayout(false);
        this.PerformLayout();
        tableLayoutPanel1.ResumeLayout(false);
        tableLayoutPanel1.PerformLayout();
        tableLayoutPanel2.ResumeLayout(false);
        tableLayoutPanel2.PerformLayout();
        tableLayoutPanel4.ResumeLayout(false);
        tableLayoutPanel4.PerformLayout();
        tableLayoutPanel5.ResumeLayout(false);
        tableLayoutPanel5.PerformLayout();
        
        string[] s = SerialPort.GetPortNames();
        comboBox2.Items.AddRange(s);
        comboBox2.SelectedIndex = (s.Length-1);
    }
    
    private BackgroundWorker backgroundWorker1;
    private OpenFileDialog openFileDialog1;
    private Button button2;
    private ProgressBar progressBar1;
    private Label label5;
    private TableLayoutPanel tableLayoutPanel5;
    private ComboBox comboBox3;
    private Label label4;
    private ComboBox comboBox2;
    private Label label3;
    private TableLayoutPanel tableLayoutPanel4;
    private Button button1;
    private TextBox textBox1;
    private Label label1;
    private TableLayoutPanel tableLayoutPanel2;
    private TableLayoutPanel tableLayoutPanel1;
}
