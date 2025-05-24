using System;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using MathNet.Numerics.IntegralTransforms;

namespace EstroboAudioArduinoWF
{
    public partial class Form1 : Form
    {
        SerialPort serialPort;
        WaveInEvent waveIn;
        readonly object serialLock = new object();
        volatile bool isConnected = false;
        string comPort = "COM7";

        double bassMax = 0.01, midMax = 0.01, highMax = 0.01;
        int currentBass = 0, currentMid = 0, currentHigh = 0;

        bool enableBass = true, enableMid = true, enableHigh = true;

        double bassMultiplier = 1.0;
        double midMultiplier = 0.8;
        double highMultiplier = 1.0;

        NotifyIcon trayIcon;
        ContextMenuStrip trayMenu;
        ToolStripMenuItem bassItem, midItem, highItem;

        HttpListener httpListener;
        const string PREFIX = "http://localhost:5855/";

        public Form1()
        {
            InitializeComponent();
            var ini = new IniFile();
            comPort = ini.Read("Serial", "Port");

            InitializeTrayIcon();
            StartAudioCapture();
            StartSenderLoop();
            StartHttpServer();

            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Visible = false;
        }

        private void InitializeTrayIcon()
        {
            trayMenu = new ContextMenuStrip();

            bassItem = new ToolStripMenuItem("Bajos (ON)") { Checked = true };
            bassItem.Click += (s, e) => ToggleBass();

            midItem = new ToolStripMenuItem("Medios (ON)") { Checked = true };
            midItem.Click += (s, e) => ToggleMid();

            highItem = new ToolStripMenuItem("Altos (ON)") { Checked = true };
            highItem.Click += (s, e) => ToggleHigh();

            trayMenu.Items.Add(bassItem);
            trayMenu.Items.Add(midItem);
            trayMenu.Items.Add(highItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Salir", null, (s, e) => Application.Exit());

            trayIcon = new NotifyIcon
            {
                Text = "Estrobo Audio Arduino",
                Icon = SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
        }

        private void ToggleBass()
        {
            enableBass = !enableBass;
            bassItem.Checked = enableBass;
            bassItem.Text = $"Bajos ({(enableBass ? "ON" : "OFF")})";
        }

        private void ToggleMid()
        {
            enableMid = !enableMid;
            midItem.Checked = enableMid;
            midItem.Text = $"Medios ({(enableMid ? "ON" : "OFF")})";
        }

        private void ToggleHigh()
        {
            enableHigh = !enableHigh;
            highItem.Checked = enableHigh;
            highItem.Text = $"Altos ({(enableHigh ? "ON" : "OFF")})";
        }

        private void StartAudioCapture()
        {
            Task.Run(() => TryOpenSerialPort());

            waveIn = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(44100, 1),
                BufferMilliseconds = 50
            };
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.StartRecording();
        }

        private void TryOpenSerialPort()
        {
            while (true)
            {
                try
                {
                    lock (serialLock)
                    {
                        serialPort?.Close();
                        serialPort?.Dispose();
                        serialPort = new SerialPort(comPort, 9600);
                        serialPort.Open();
                        isConnected = true;
                        trayIcon.ShowBalloonTip(2000, "Arduino Conectado",
                            $"Puerto {comPort} conectado exitosamente.", ToolTipIcon.Info);
                    }
                    break;
                }
                catch
                {
                    isConnected = false;
                    trayIcon.ShowBalloonTip(2000, "Esperando conexión",
                        $"No se encontró {comPort}. Reintentando...", ToolTipIcon.Warning);
                    Thread.Sleep(5000);
                }
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            var buffer = new float[e.BytesRecorded / 2];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;

            if (buffer.Average(x => x * x) < 0.00001)
            {
                currentBass = currentMid = currentHigh = 0;
                return;
            }

            int fftSize = 1 << (int)Math.Floor(Math.Log(buffer.Length, 2));
            var samples = buffer.Take(fftSize).Select(f => new Complex(f, 0)).ToArray();
            Fourier.Forward(samples, FourierOptions.Matlab);

            var mags = samples.Take(fftSize / 2).Select(c => c.Magnitude).ToArray();
            double bin = 44100.0 / fftSize;

            double bass = enableBass ? mags.Take((int)(150 / bin)).Max() : 0;
            double mid = enableMid ? mags.Skip((int)(150 / bin)).Take((int)((2000 - 150) / bin)).Max() : 0;
            double high = enableHigh ? mags.Skip((int)(2000 / bin)).Max() : 0;

            bassMax = Math.Max(bassMax * 0.95, bass);
            midMax = Math.Max(midMax * 0.95, mid);
            highMax = Math.Max(highMax * 0.95, high);

            int tBass = Math.Clamp((int)(bass / bassMax * 255), 0, 255);
            int tMid = Math.Clamp((int)(mid / midMax * 255), 0, 255);
            int tHigh = Math.Clamp((int)(high / highMax * 255), 0, 255);

            double smoothing = 0.9;
            currentBass = (int)(currentBass * smoothing + tBass * (1 - smoothing));
            currentMid = (int)(currentMid * smoothing + tMid * (1 - smoothing));
            currentHigh = (int)(currentHigh * smoothing + tHigh * (1 - smoothing));
        }

        private int ApplyGamma(double value, double gamma = 2.2)
        {
            return (int)(Math.Pow(Math.Clamp(value / 255.0, 0, 1), gamma) * 255);
        }

        private void StartSenderLoop()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    if (isConnected)
                    {
                        try
                        {
                            lock (serialLock)
                            {
                                serialPort.Write(new byte[]
                                {
                                    (byte)ApplyGamma(enableBass ? currentBass * bassMultiplier : 0),
                                    (byte)ApplyGamma(enableMid  ? currentMid  * midMultiplier  : 0),
                                    (byte)ApplyGamma(enableHigh ? currentHigh * highMultiplier : 0)
                                }, 0, 3);
                            }
                        }
                        catch
                        {
                            isConnected = false;
                            trayIcon.ShowBalloonTip(2000, "Arduino desconectado",
                                "El dispositivo se desconectó. Reintentando conexión...", ToolTipIcon.Warning);
                            Task.Run(() => TryOpenSerialPort());
                        }
                    }
                    Thread.Sleep(10);
                }
            });
        }

        private void StartHttpServer()
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add(PREFIX);
            httpListener.Start();

            Task.Run(async () =>
            {
                while (httpListener.IsListening)
                {
                    var ctx = await httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleHttpRequest(ctx));
                }
            });
        }

        private void HandleHttpRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/leds")
            {
                try
                {
                    using var sr = new StreamReader(req.InputStream, req.ContentEncoding);
                    var body = sr.ReadToEnd();
                    var doc = JsonDocument.Parse(body).RootElement;

                    if (doc.TryGetProperty("bass", out var b)) enableBass = b.GetBoolean();
                    if (doc.TryGetProperty("mid", out var m)) enableMid = m.GetBoolean();
                    if (doc.TryGetProperty("high", out var h)) enableHigh = h.GetBoolean();

                    this.Invoke(() =>
                    {
                        bassItem.Checked = enableBass;
                        bassItem.Text = $"Bajos ({(enableBass ? "ON" : "OFF")})";
                        midItem.Checked = enableMid;
                        midItem.Text = $"Medios ({(enableMid ? "ON" : "OFF")})";
                        highItem.Checked = enableHigh;
                        highItem.Text = $"Altos ({(enableHigh ? "ON" : "OFF")})";
                    });

                    resp.StatusCode = 200;
                    var ok = Encoding.UTF8.GetBytes("OK");
                    resp.OutputStream.Write(ok, 0, ok.Length);
                }
                catch
                {
                    resp.StatusCode = 400;
                }
            }
            else
            {
                resp.StatusCode = 404;
            }
            resp.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            httpListener?.Stop();
            httpListener?.Close();

            trayIcon.Visible = false;
            waveIn?.StopRecording();
            lock (serialLock) { serialPort?.Close(); }
            base.OnFormClosing(e);
        }

        private void Form1_Load(object sender, EventArgs e) { }
    }
}
