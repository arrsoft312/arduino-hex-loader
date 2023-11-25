using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyVersion(xLoader2.AppVersion)]
[assembly: ComVisible(false)]

[assembly: AssemblyTitle(xLoader2.AppTitle)]
[assembly: AssemblyDescription(xLoader2.AppDescription)]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany(xLoader2.AppAuthor)]
[assembly: AssemblyProduct(xLoader2.AppTitle)]
[assembly: AssemblyCopyright(xLoader2.AppCopyright)]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyFileVersion(xLoader2.AppVersion)]

class xLoader2:Form {
    public const string AppTitle = "Arduino HEX Loader";
    public const string AppDescription = "A utility for loading HEX files into Arduino Uno, Nano and Mega 2560 boards";
    public const string AppVersion = "2.1.0";
    public const string AppVersionBuild = "2023-11-25";
    public const string AppAuthor = "Artur Kurpukov";
    public const string AppCopyright = "Copyright (C) 2021-2023 Artur Kurpukov";
    
    private readonly ResourceManager _resources = new ResourceManager(typeof(xLoader2));
    private readonly IContainer _components = new Container();
    
    private readonly SerialPort _serialPort1;
    
    private readonly int[][] _avr_parts = new int[][] {
        new int[] { 0x1e950F, 128, 32768, }, // ATmega328P
        new int[] { 0x1e9801, 256, 262144, }, // ATmega2560
        new int[] { 0x1e9705, 256, 131072, }, // ATmega1284P
    };
    
    private string _fileName;
    
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
        button3.Enabled = false;
        button4.Enabled = false;
        
        backgroundWorker1.RunWorkerAsync();
    }
    
    private void Button3Click(object sender, EventArgs e) {
        string s = (AppTitle + " v" + AppVersion + " (" + AppVersionBuild + ")\n\n" + AppCopyright + "\n");
        s += "\nIf you found this utility useful, make a small donation.\n";
        
        MessageBox.Show(this, s, ("About " + AppTitle), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    
    private void Button4Click(object sender, EventArgs e) {
        Process.Start("https://github.com/arrsoft312/arduino-hex-loader");
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
        
        MemoryMap flash = new MemoryMap(_fileName);
        int size = flash.Size;
        
        byte[] buf = new byte[512];
        
        _serialPort1.Open();
        try {
            _serialPort1.DtrEnable = true;
            _serialPort1.RtsEnable = true;
            Thread.Sleep(50);
            
            _serialPort1.DtrEnable = false;
            _serialPort1.RtsEnable = false;
            Thread.Sleep(150);
            
            _serialPort1.DiscardInBuffer();
            
            int jj = -1;
            
            _serialPort1.ReadTimeout = 300;
            
            _serialPort1.Write(new byte[] { 0x50, 0x20, }, 0, 2);
            
            try {
                jj = _serialPort1.ReadByte();
                int resp2 = _serialPort1.ReadByte();
                
                if (jj != 0x14 || resp2 != 0x10) {
                    throw new WarningException("Invalid response from bootloader!");
                }
            } catch (TimeoutException) {
                if (jj != -1) {
                    throw;
                }
            }
            
            if (jj == -1) {
                const byte MESSAGE_START = 0x1B;
                const byte TOKEN = 0x0E;
                
                const byte CMD_LOAD_ADDRESS = 0x06;
                
                const byte CMD_ENTER_PROGMODE_ISP = 0x10;
                const byte CMD_LEAVE_PROGMODE_ISP = 0x11;
                const byte CMD_PROGRAM_FLASH_ISP = 0x13;
                const byte CMD_READ_FLASH_ISP = 0x14;
                const byte CMD_READ_SIGNATURE_ISP = 0x1B;
                
                const byte STATUS_CMD_OK = 0x00;
                
                _serialPort1.ReadTimeout = 1500;
                
                int signature = 0;
                int page_size = 0;
                
                for (int addr = -4, k = 0;;) {
                    int i = 5;
                    int j = 2;
                    
                    if (addr < -3) {
                        buf[i++] = CMD_ENTER_PROGMODE_ISP;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                    } else if (addr < 0) {
                        buf[i++] = CMD_READ_SIGNATURE_ISP;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                        buf[i++] = (byte)(3+addr);
                        buf[i++] = 0x00;
                        j = 4;
                    } else if (addr < size) {
                        switch (k) {
                            case 0:
                            case 2:
                                buf[i++] = CMD_LOAD_ADDRESS;
                                buf[i++] = (byte)(addr >> 25);
                                buf[i++] = (byte)(addr >> 17);
                                buf[i++] = (byte)(addr >> 9);
                                buf[i++] = (byte)(addr >> 1);
                                break;
                            case 1:
                                buf[i++] = CMD_PROGRAM_FLASH_ISP;
                                buf[i++] = (byte)(page_size >> 8);
                                buf[i++] = (byte)page_size;
                                buf[i++] = 0x00;
                                buf[i++] = 0x00;
                                buf[i++] = 0x00;
                                buf[i++] = 0x00;
                                buf[i++] = 0x00;
                                buf[i++] = 0x00;
                                buf[i++] = 0x00;
                                for (int n = 0; n < page_size; n++) {
                                    buf[i++] = flash[addr+n];
                                }
                                break;
                            default:
                                buf[i++] = CMD_READ_FLASH_ISP;
                                buf[i++] = (byte)(page_size >> 8);
                                buf[i++] = (byte)page_size;
                                buf[i++] = 0x00;
                                j = (3+page_size);
                                break;
                        }
                    } else {
                        buf[i++] = CMD_LEAVE_PROGMODE_ISP;
                        buf[i++] = 0x00;
                        buf[i++] = 0x00;
                    }
                    
                    byte checksum = 0;
                    i -= 5;
                    
                    buf[0] = MESSAGE_START;
                    buf[1] = 1;
                    buf[2] = (byte)(i >> 8);
                    buf[3] = (byte)i;
                    buf[4] = TOKEN;
                    
                    for (int n = 0; n < (5+i); n++) {
                        checksum ^= buf[n];
                    }
                    
                    buf[5+i] = checksum;
                    
                    _serialPort1.Write(buf, 0, i+6);
                    
                    checksum = 0;
                    i = 2;
                    
                    buf[2] = (byte)(j >> 8);
                    buf[3] = (byte)j;
                    buf[6] = STATUS_CMD_OK;
                    
                    for (int n = 0;;) {
                        byte ch = (byte)_serialPort1.ReadByte();
                        checksum ^= ch;
                        
                        if (n < 7) {
                            if (ch != buf[n]) {
                                throw new WarningException("Invalid response from bootloader!");
                            }
                            ++n;
                        } else if (i < j) {
                            buf[i++] = ch;
                        } else {
                            if (checksum != 0) {
                                throw new WarningException("Invalid response from bootloader!");
                            }
                            break;
                        }
                    }
                    
                    if (addr < -3) {
                        ++addr;
                    } else if (addr < 0) {
                        ++addr;
                        signature |= (buf[2] << (-8*addr));
                        
                        //if (buf[3] != STATUS_CMD_OK) {
                            //throw new WarningException("Invalid response from bootloader!");
                        //}
                        
                        if (addr == 0) {
                            foreach (int[] avr_part in _avr_parts) {
                                if (avr_part[0] == signature) {
                                    page_size = avr_part[1];
                                    break;
                                }
                            }
                            
                            if (page_size == 0) {
                                throw new WarningException("Unknown MCU signature!");
                            }
                            
                            size = ((size + (page_size-1)) / page_size * page_size);
                        }
                    } else if (addr < size) {
                        if (++k == 4) {
                            for (int n = 0; n < page_size; n++) {
                                if (buf[2+n] != flash[addr+n]) {
                                    throw new WarningException(String.Format("Verification error! First content mismatch at address 0x{0:X6}, 0x{1:x2} != 0x{2:x2}!", addr+n, buf[2+n], flash[addr+n]));
                                }
                            }
                            
                            //if (buf[2+n] != STATUS_CMD_OK) {
                                //throw new WarningException("Invalid response from bootloader!");
                            //}
                            
                            addr += page_size;
                            k = 0;
                            
                            ((BackgroundWorker)sender).ReportProgress(1000 * addr / size);
                        }
                    } else {
                        break;
                    }
                }
            } else {
                const byte Resp_STK_OK = 0x10;
                const byte Resp_STK_INSYNC = 0x14;
                
                const byte Sync_CRC_EOP = 0x20;
                
                const byte Cmnd_STK_ENTER_PROGMODE = 0x50;
                const byte Cmnd_STK_LEAVE_PROGMODE = 0x51;
                const byte Cmnd_STK_LOAD_ADDRESS = 0x55;
                const byte Cmnd_STK_UNIVERSAL = 0x56;
                const byte Cmnd_STK_PROG_PAGE = 0x64;
                const byte Cmnd_STK_READ_PAGE = 0x74;
                const byte Cmnd_STK_READ_SIGN = 0x75;
                
                _serialPort1.ReadTimeout = 1500;
                
                int page_size = 0;
                
                for (int addr = -1, k = -1;;) {
                    int i = 0;
                    int j = 0;
                    
                    if (addr < 0) {
                        buf[i++] = Cmnd_STK_READ_SIGN;
                        buf[i++] = Sync_CRC_EOP;
                        j = 3;
                    } else if (addr < size) {
                        switch (k) {
                            case -1:
                                buf[i++] = Cmnd_STK_UNIVERSAL;
                                buf[i++] = 0x4d;
                                buf[i++] = 0x00;
                                buf[i++] = (byte)(addr >> 17);
                                buf[i++] = 0x00;
                                buf[i++] = Sync_CRC_EOP;
                                j = 1;
                                break;
                            case 0:
                            case 2:
                                buf[i++] = Cmnd_STK_LOAD_ADDRESS;
                                buf[i++] = (byte)(addr >> 1);
                                buf[i++] = (byte)(addr >> 9);
                                buf[i++] = Sync_CRC_EOP;
                                break;
                            case 1:
                                buf[i++] = Cmnd_STK_PROG_PAGE;
                                buf[i++] = (byte)(page_size >> 8);
                                buf[i++] = (byte)page_size;
                                buf[i++] = (byte)'F';
                                for (int n = 0; n < page_size; n++) {
                                    buf[i++] = flash[addr+n];
                                }
                                buf[i++] = Sync_CRC_EOP;
                                break;
                            default:
                                buf[i++] = Cmnd_STK_READ_PAGE;
                                buf[i++] = (byte)(page_size >> 8);
                                buf[i++] = (byte)page_size;
                                buf[i++] = (byte)'F';
                                buf[i++] = Sync_CRC_EOP;
                                j = page_size;
                                break;
                        }
                    } else {
                        buf[i++] = (byte)'Q';
                        buf[i++] = Sync_CRC_EOP;
                    }
                    
                    _serialPort1.Write(buf, 0, i);
                    
                    byte resp = (byte)_serialPort1.ReadByte();
                    for (i = 0; i < j; i++) {
                        buf[i] = (byte)_serialPort1.ReadByte();
                    }
                    byte resp2 = (byte)_serialPort1.ReadByte();
                    
                    if (resp != Resp_STK_INSYNC || resp2 != Resp_STK_OK) {
                        throw new WarningException("Invalid response from bootloader!");
                    }
                    
                    if (addr < 0) {
                        int signature = ((buf[0] << 16) | (buf[1] << 8) | (buf[2] << 0));
                        foreach (int[] avr_part in _avr_parts) {
                            if (avr_part[0] == signature) {
                                page_size = avr_part[1];
                                break;
                            }
                        }
                        
                        if (page_size == 0) {
                            throw new WarningException("Unknown MCU signature!");
                        }
                        
                        size = ((size + (page_size-1)) / page_size * page_size);
                        
                        addr = 0;
                    } else if (addr < size) {
                        if (++k == 4) {
                            for (int n = 0; n < page_size; n++) {
                                if (buf[n] != flash[addr+n]) {
                                    throw new WarningException(String.Format("Verification error! First content mismatch at address 0x{0:X6}, 0x{1:x2} != 0x{2:x2}!", addr+n, buf[n], flash[addr+n]));
                                }
                            }
                            
                            addr += page_size;
                            if ((addr % 131072) == 0) {
                                k = -1;
                            } else {
                                k = 0;
                            }
                            
                            ((BackgroundWorker)sender).ReportProgress(1000 * addr / size);
                        }
                    } else {
                        break;
                    }
                }
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
            MessageBox.Show(this, err.Message, AppTitle, MessageBoxButtons.OK, (err is WarningException ? MessageBoxIcon.Warning : MessageBoxIcon.Error));
        } else {
            MessageBox.Show(this, "Target updated successfully!", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        
        button1.Enabled = true;
        comboBox2.Enabled = true;
        comboBox3.Enabled = true;
        progressBar1.Value = 0;
        button2.Enabled = true;
        button3.Enabled = true;
        button4.Enabled = true;
    }
    
    [STAThread]
    internal static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new xLoader2());
    }
    
    protected override void Dispose(bool disposing) {
        if (disposing) {
            _components.Dispose();
        }
        base.Dispose(disposing);
    }
    
    public xLoader2() {
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
        
        tableLayoutPanel6 = new TableLayoutPanel();
        button2 = new Button();
        button3 = new Button();
        button4 = new Button();
        
        openFileDialog1 = new OpenFileDialog();
        openFileDialog1.Title = AppTitle;
        openFileDialog1.ReadOnlyChecked = true;
        openFileDialog1.Filter = "HEX files (*.hex)|*.hex|All files (*.*)|*.*";
        
        backgroundWorker1 = new BackgroundWorker();
        backgroundWorker1.WorkerReportsProgress = true;
        backgroundWorker1.DoWork += BackgroundWorker1DoWork;
        backgroundWorker1.ProgressChanged += BackgroundWorker1ProgressChanged;
        backgroundWorker1.RunWorkerCompleted += BackgroundWorker1RunWorkerCompleted;
        
        _serialPort1 = new SerialPort(_components);
        
        tableLayoutPanel2.SuspendLayout();
        tableLayoutPanel4.SuspendLayout();
        tableLayoutPanel5.SuspendLayout();
        tableLayoutPanel6.SuspendLayout();
        tableLayoutPanel1.SuspendLayout();
        this.SuspendLayout();
        
        tableLayoutPanel1.AutoSize = true;
        tableLayoutPanel1.ColumnCount = 1;
        tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 0, 0);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel4, 0, 1);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel5, 0, 2);
        tableLayoutPanel1.Controls.Add(tableLayoutPanel6, 0, 3);
        tableLayoutPanel1.Dock = DockStyle.Fill;
        tableLayoutPanel1.RowCount = 4;
        tableLayoutPanel1.RowStyles.Add(new RowStyle());
        tableLayoutPanel1.RowStyles.Add(new RowStyle());
        tableLayoutPanel1.RowStyles.Add(new RowStyle());
        tableLayoutPanel1.RowStyles.Add(new RowStyle());
        tableLayoutPanel1.TabIndex = 0;
        
        tableLayoutPanel2.AutoSize = true;
        tableLayoutPanel2.ColumnCount = 2;
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel2.Controls.Add(label1, 0, 0);
        tableLayoutPanel2.Controls.Add(textBox1, 0, 1);
        tableLayoutPanel2.Controls.Add(button1, 1, 1);
        tableLayoutPanel2.Dock = DockStyle.Fill;
        tableLayoutPanel2.Margin = new Padding(8, 8, 8, 3);
        tableLayoutPanel2.RowCount = 2;
        tableLayoutPanel2.RowStyles.Add(new RowStyle());
        tableLayoutPanel2.RowStyles.Add(new RowStyle());
        tableLayoutPanel2.TabIndex = 0;
        
        label1.AutoSize = true;
        label1.Dock = DockStyle.Fill;
        label1.TabIndex = 0;
        label1.Text = "HEX &file:";
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
        tableLayoutPanel4.ColumnCount = 4;
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel4.Controls.Add(label3, 0, 0);
        tableLayoutPanel4.Controls.Add(comboBox2, 0, 1);
        tableLayoutPanel4.Controls.Add(label4, 2, 0);
        tableLayoutPanel4.Controls.Add(comboBox3, 2, 1);
        tableLayoutPanel4.Dock = DockStyle.Fill;
        tableLayoutPanel4.Margin = new Padding(8, 2, 8, 3);
        tableLayoutPanel4.RowCount = 2;
        tableLayoutPanel4.RowStyles.Add(new RowStyle());
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
        comboBox3.Text = "115200";
        
        tableLayoutPanel5.AutoSize = true;
        tableLayoutPanel5.ColumnCount = 1;
        tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel5.Controls.Add(label5, 0, 0);
        tableLayoutPanel5.Controls.Add(progressBar1, 0, 1);
        tableLayoutPanel5.Dock = DockStyle.Fill;
        tableLayoutPanel5.Margin = new Padding(8, 2, 8, 3);
        tableLayoutPanel5.RowCount = 2;
        tableLayoutPanel5.RowStyles.Add(new RowStyle());
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
        
        tableLayoutPanel6.AutoSize = true;
        tableLayoutPanel6.ColumnCount = 4;
        tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle());
        tableLayoutPanel6.Controls.Add(button2, 0, 0);
        tableLayoutPanel6.Controls.Add(button3, 1, 0);
        tableLayoutPanel6.Controls.Add(button4, 3, 0);
        tableLayoutPanel6.Dock = DockStyle.Fill;
        tableLayoutPanel6.Margin = new Padding(8, 2, 8, 8);
        tableLayoutPanel6.RowCount = 1;
        tableLayoutPanel6.RowStyles.Add(new RowStyle());
        tableLayoutPanel6.TabIndex = 3;
        
        button2.AutoSize = true;
        button2.Dock = DockStyle.Fill;
        button2.Enabled = false;
        button2.Size = new Size(-1, 24);
        button2.TabIndex = 0;
        button2.Text = "&Upload";
        button2.UseVisualStyleBackColor = true;
        button2.Click += Button2Click;
        
        button3.AutoSize = true;
        button3.Dock = DockStyle.Fill;
        button3.Enabled = true;
        button3.Size = new Size(-1, 24);
        button3.TabIndex = 1;
        button3.Text = "&About";
        button3.UseVisualStyleBackColor = true;
        button3.Click += Button3Click;
        
        button4.AutoSize = true;
        button4.Dock = DockStyle.Fill;
        button4.Enabled = true;
        button4.FlatAppearance.BorderSize = 0;
        button4.FlatStyle = FlatStyle.Flat;
        button4.Image = (Image)_resources.GetObject("github");
        button4.Size = new Size(24, 24);
        button4.TabIndex = 2;
        button4.TabStop = false;
        button4.UseVisualStyleBackColor = true;
        button4.Click += Button4Click;
        
        this.AcceptButton = button2;
        this.AllowDrop = true;
        this.AutoScaleDimensions = new SizeF(6F, 13F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.AutoSize = true;
        this.ClientSize = new Size(240, -1);
        this.Controls.Add(tableLayoutPanel1);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Text = AppTitle;
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
        tableLayoutPanel6.ResumeLayout(false);
        tableLayoutPanel6.PerformLayout();
        
        string[] s = SerialPort.GetPortNames();
        comboBox2.Items.AddRange(s);
        comboBox2.SelectedIndex = (s.Length-1);
    }
    
    private BackgroundWorker backgroundWorker1;
    private OpenFileDialog openFileDialog1;
    private Button button4;
    private Button button3;
    private Button button2;
    private TableLayoutPanel tableLayoutPanel6;
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
