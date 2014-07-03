using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumenera.USB;

namespace LumeneraDisplay
{
    class LumeneraCamera : IDisposable
    {
        private readonly int _handle;
        private dll.LucamSnapshot _snapshot;
        private bool _changed = true;
        private readonly ushort[] _data;
        private readonly int _width;
        private bool _disposed;

        private LumeneraCamera(int handle, dll.LucamSnapshot snapshot, ushort[] data, int width)
        {
            _handle = handle;
            _snapshot = snapshot;
            _data = data;
            _width = width;

            ResetSettings();
        }

        public static LumeneraCamera Create(int id)
        {
            var camera = dll.LucamCameraOpen(id);
            if (camera == 0)
                return null;

            api.SetFrameRate(camera, api.GetFrameRates(camera).Min());

            var snapshot = new dll.LucamSnapshot
            {
                BufferLastFrame = false,
                Exposure = 1000,
                ExposureDelay = 0,
                Format =
                {
                    BinningX = 1,
                    BinningY = 1,
                    FlagsX = 0,
                    FlagsY = 0,
                    Width = (int)GetProperty(camera, dll.LucamProperty.MAX_WIDTH),
                    Height = (int)GetProperty(camera, dll.LucamProperty.MAX_HEIGHT),
                    PixelFormat = dll.LucamPixelFormat.PF_16,
                    SubSampleX = 1,
                    SubSampleY = 1
                },
                Gain = 4,
                ShutterType = dll.LucamShutterType.GlobalShutter,
                StrobeDelay = 0.1f,
                StrobeFlags = 0,
                UseHwTrigger = false,
                Timeout = -1,
                GainBlue = 1,
                GainCyan = 1,
                GainMagenta = 1,
                GainGrn1 = 1,
                GainGrn2 = 1,
                GainRed = 1,
                GainYel1 = 1,
                GainYel2 = 1
            };

            return new LumeneraCamera(camera, snapshot, new ushort[snapshot.Format.Width * snapshot.Format.Height], snapshot.Format.Width);
        }

        public void Snap()
        {
            if (_changed)
            {
                dll.LucamDisableFastFrames(_handle);
                dll.LucamEnableFastFrames(_handle, ref _snapshot);
                _changed = false;
            }
            api.TakeFastFrame(_handle, _data);
        }

        public float Gain
        {
            get { return _snapshot.Gain; }
            set
            {
                _snapshot.Gain = value;
                _changed = true;
            }
        }

        public float Exposure
        {
            get { return _snapshot.Exposure / 1000; }
            set
            {
                _snapshot.Exposure = value * 1000;
                _changed = true;
            }
        }

        public ushort[] Data
        {
            get { return _data; }
        }

        public int Width { get { return _width; } }

        static float GetProperty(int camera, dll.LucamProperty property)
        {
            float value;
            dll.LucamPropertyFlag flags;
            dll.LucamGetProperty(camera, property, out value, out flags);
            return value;
        }

        public void Dispose()
        {
            if (_disposed == false)
            {
                _disposed = true;
                dll.LucamCameraClose(_handle);
            }
        }

        private void ResetSettings()
        {
            api.SetProperty(_handle, dll.LucamProperty.GAMMA, 1.0f);
            api.SetProperty(_handle, dll.LucamProperty.DIGITAL_GAIN, 1.0f);
            api.SetProperty(_handle, dll.LucamProperty.CONTRAST, 1.0f);
            api.SetProperty(_handle, dll.LucamProperty.BRIGHTNESS, 1.0f);
        }
    }

    class Program
    {
        static LumeneraCamera _camera;
        private static float _liveExposure = 1.0f;
        private static float _saveExposure = 1.0f;
        private static string _savedir;
        private static volatile int _save;

        static void Main()
        {
            new Thread(() =>
            {
                var saving = false;
                while (true)
                {
                    if (_camera != null)
                    {
                        if (_save != 0 && saving == false)
                            _camera.Exposure = _saveExposure;
                        else if (_save == 0 && saving)
                            _camera.Exposure = _liveExposure;
                        saving = _save != 0;
                        try
                        {
                            _camera.Snap();
                        }
                        catch (Exception e)
                        {
                            _camera.Dispose();
                            _camera = null;
                            Console.WriteLine("Disconnected from camera");
                            Console.WriteLine(e.GetType().Name + ": " + e.Message);
                            continue;
                        }
                        if (saving)
                        {
                            _save--;
                            _save = Math.Max(_save, 0);
                            ImageSaver.Save(_savedir, _camera.Data, _camera.Width);
                            Console.WriteLine("Saved image, {0} to go", _save);
                        }
                        DisplayWindow.Set(_camera.Data, _camera.Width);
                    }
                    else
                        Thread.Sleep(1000);
                }
            }) { IsBackground = true }.Start();
            while (true)
            {
                Console.WriteLine("Commands: connect, cross, zoom, gain, stretch, exposure, save, saveexp, savedir");
                Console.Write("> ");
                var readLine = Console.ReadLine();
                if (readLine == null)
                    break;
                var line = readLine.Split(null);
                switch (line[0])
                {
                    case "connect":
                        Console.WriteLine("Connecting");
                        var newCamera = LumeneraCamera.Create(1);
                        if (newCamera == null)
                            Console.WriteLine("Couldn't connect to camera number 1");
                        else
                            _camera = newCamera;
                        break;
                    case "cross":
                        DisplayWindow.Cross = !DisplayWindow.Cross;
                        break;
                    case "zoom":
                        int newZoom;
                        if (line.Length == 2 && int.TryParse(line[1], out newZoom) && newZoom > 0)
                            DisplayWindow.Zoom = newZoom;
                        else if (DisplayWindow.Zoom == 0)
                            DisplayWindow.Zoom = 80;
                        else
                            DisplayWindow.Zoom = 0;
                        break;
                    case "gain":
                        float resultGain;
                        if (_camera != null && line.Length == 2 && float.TryParse(line[1], out resultGain))
                            _camera.Gain = resultGain;
                        else
                            Console.WriteLine("Bad gain command, syntax: gain [number]");
                        break;
                    case "stretch":
                        double newStretch;
                        if (line.Length == 2 && double.TryParse(line[1], out newStretch) && newStretch > 0)
                            DisplayWindow.AutoStretchAmount = newStretch;
                        else if (DisplayWindow.AutoStretchAmount <= 0)
                            DisplayWindow.AutoStretchAmount = 0.5;
                        else
                            DisplayWindow.AutoStretchAmount = 0;
                        break;
                    case "exposure":
                        float resultExposure;
                        if (_camera != null && line.Length == 2 && float.TryParse(line[1], out resultExposure))
                        {
                            _liveExposure = resultExposure;
                            if (_save == 0)
                                _camera.Exposure = _liveExposure;
                        }
                        else
                            Console.WriteLine("Bad exposure command, syntax: exposure [seconds]");
                        break;
                    case "save":
                        int resultSave;
                        if (_camera != null && line.Length == 2 && int.TryParse(line[1], out resultSave) &&
                            resultSave > 0)
                            _save = resultSave;
                        else
                            Console.WriteLine("Bad save command, syntax: save [number]");
                        break;
                    case "saveexp":
                        float saveExp;
                        if (_camera != null && line.Length == 2 && float.TryParse(line[1], out saveExp))
                            _saveExposure = saveExp;
                        else
                            Console.WriteLine("Bad saveexp command, syntax: saveexp [exposure in seconds]");
                        break;
                    case "savedir":
                        if (line.Length == 1)
                            _savedir = null;
                        else if (line.Length == 2)
                            _savedir = line[1];
                        else
                            Console.WriteLine("Bad savedir command");
                        break;
                    default:
                        Console.WriteLine("Unknown command " + line[0]);
                        break;
                }
            }
        }
    }

    static class ImageSaver
    {
        public static void Save(string snapDirectory, ushort[] data, int width)
        {
            Snap(GetTelescopeFilename(snapDirectory), data, width);
        }

        private static void Snap(string filename, ushort[] data, int width)
        {
            var tempWritableBitmap = new WriteableBitmap(width, data.Length / width, 96, 96, PixelFormats.Gray16, null);
            tempWritableBitmap.Lock();
            Marshal.Copy(Array.ConvertAll(data, s => (short)s), 0, tempWritableBitmap.BackBuffer, data.Length);
            tempWritableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, data.Length / width));
            tempWritableBitmap.Unlock();

            var encoder = new TiffBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(tempWritableBitmap));
            var filestream = File.Open(filename, FileMode.OpenOrCreate);
            encoder.Save(filestream);
            filestream.Close();
        }

        private static string GetTelescopeFilename(string snapDirectory)
        {
            var now = DateTime.Now;
            var file = string.Format("telescope.{0}-{1}.{2}-{3}-{4}", now.Month, now.Day, now.Hour, now.Minute, now.Second);
            file = string.IsNullOrWhiteSpace(snapDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), file)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), snapDirectory, file);
            var directory = Path.GetDirectoryName(file);
            if (directory != null && Directory.Exists(directory) == false)
                Directory.CreateDirectory(directory);
            file = GetUniqueFilename(file, ".tiff");
            return file;
        }

        private static string GetUniqueFilename(string baseFile, string ext)
        {
            var file = baseFile + ext;
            if (File.Exists(file) == false)
                return file;
            var i = 1;
            while (File.Exists(file = baseFile + "." + i + ext))
                i++;
            return file;
        }
    }

    class DisplayWindow : Form
    {
        private static DisplayWindow _fetch;
        private readonly Bitmap _bitmap;
        private readonly object _lock = new object();
        private readonly System.Drawing.Brush _fillBrush = System.Drawing.Brushes.Black;
        private readonly System.Drawing.Pen _crossBrush = Pens.Red;

        public DisplayWindow(int width, int height)
        {
            _bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            int width, height;
            lock (_lock)
            {
                var clientSize = ClientSize;
                if (clientSize.Width >= _bitmap.Width && Height >= _bitmap.Height && Zoom <= 0)
                {
                    height = _bitmap.Height;
                    width = _bitmap.Width;
                    e.Graphics.DrawImageUnscaled(_bitmap, 0, 0);
                    e.Graphics.FillRectangle(_fillBrush, _bitmap.Width, 0, clientSize.Width - _bitmap.Width, clientSize.Height);
                    e.Graphics.FillRectangle(_fillBrush, 0, _bitmap.Height, clientSize.Width, clientSize.Height - _bitmap.Height);
                }
                else
                {
                    height = Math.Min(clientSize.Height, clientSize.Width * _bitmap.Height / _bitmap.Width);
                    width = height * _bitmap.Width / _bitmap.Height;
                    if (Zoom > 0)
                        e.Graphics.DrawImage(_bitmap, new Rectangle(0, 0, width, height),
                            new Rectangle(_bitmap.Width / 2 - Zoom, _bitmap.Height / 2 - Zoom, Zoom * 2, Zoom * 2),
                            GraphicsUnit.Pixel);
                    else
                        e.Graphics.DrawImage(_bitmap, 0, 0, width, height);
                    if (height == clientSize.Height)
                        e.Graphics.FillRectangle(_fillBrush, width, 0, clientSize.Width - width, clientSize.Height);
                    else
                        e.Graphics.FillRectangle(_fillBrush, 0, height, clientSize.Width, clientSize.Height - height);
                }
            }
            if (Cross)
            {
                e.Graphics.DrawLine(_crossBrush, width / 2, 0, width / 2, height);
                e.Graphics.DrawLine(_crossBrush, 0, height / 2, width, height / 2);
            }
        }

        public static bool Cross { get; set; }
        public static double AutoStretchAmount { get; set; }
        public static int Zoom { get; set; }

        public static void Set(ushort[] data, int width)
        {
            if (_fetch == null)
            {
                _fetch = new DisplayWindow(width, data.Length / width)
                {
                    ClientSize = new System.Drawing.Size(width, data.Length / width)
                };
                new Thread(() => _fetch.ShowDialog()) { IsBackground = true }.Start();
            }
            int[] dataConvert;
            if (AutoStretchAmount > 0)
            {
                var dataSort = new ushort[data.Length];
                Array.Copy(data, dataSort, data.Length);
                Array.Sort(dataSort);
                dataConvert = Array.ConvertAll(data, value =>
                {
                    var index = (double)Array.BinarySearch(dataSort, value) / dataSort.Length;
                    var val = (double)value / dataSort[dataSort.Length - 1];
                    val = val * (1 - AutoStretchAmount) + index * AutoStretchAmount;
                    var final = (byte)(Math.Max(Math.Min(val, 1), 0) * byte.MaxValue);
                    return final << 16 | final << 8 | final;
                });
            }
            else
            {
                dataConvert = Array.ConvertAll(data, u =>
                {
                    var value = (byte)(u * byte.MaxValue / ushort.MaxValue);
                    return value << 16 | value << 8 | value;
                });
            }
            lock (_fetch._lock)
            {
                var locked = _fetch._bitmap.LockBits(new Rectangle(0, 0, _fetch._bitmap.Width, _fetch._bitmap.Height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                Marshal.Copy(dataConvert, 0, locked.Scan0, data.Length);
                _fetch._bitmap.UnlockBits(locked);
                _fetch.BeginInvoke((Action)_fetch.Invalidate);
            }
        }
    }
}
