using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace JonsboCanvas
{
    internal sealed class NativeDisplay : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct DisplayResolution
        {
            public int Width;
            public int Height;
            public int Refresh;

            public override string ToString()
            {
                return string.Format("{0}×{1} @ {2} Hz", Width, Height, Refresh);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayPicture
        {
            public int Width;
            public int Height;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayTiming
        {
            public uint Vic, Polarity, HTotal, VTotal, HActive, VActive;
            public uint PixelClock, VerticalFrequency, HOffset, VOffset, HSyncWidth, VSyncWidth;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DisplayEdid
        {
            public int Mode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Replacement;
            public int AppendCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public DisplayTiming[] AppendedTimings;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AttachCallback(uint handle, IntPtr resolutions, int count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DetachCallback(uint handle);

        [DllImport("MSDISPLAYSDKWRRAPER.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void Wrraper_MSDisplayRegisterCallback(IntPtr attachCallback, IntPtr detachCallback);

        [DllImport("MSDISPLAYSDKWRRAPER.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int Wrraper_MSDisplayStart(int logLevel, IntPtr edid);

        [DllImport("MSDISPLAYSDKWRRAPER.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int Wrraper_MSDisplayStop();

        [DllImport("MSDISPLAYSDKWRRAPER.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "Wrraper_MSDisplaySetVideoParam")]
        private static extern int SetVideoParameter(uint handle, IntPtr resolution);

        [DllImport("MSDISPLAYSDKWRRAPER.dll", CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true, EntryPoint = "Wrraper_MSDisplaySendPicture")]
        private static extern int SendPictureNative(uint handle, IntPtr picture, bool reserved);

        private readonly object _sync = new object();
        private readonly int _preferredWidth;
        private readonly int _preferredHeight;
        private AttachCallback _attachCallback;
        private DetachCallback _detachCallback;
        private IntPtr _resolutionPointer;
        private IntPtr _picturePointer;
        private IntPtr _rotationBuffer;
        private int _rotationBufferBytes;
        private uint _handle;
        private bool _sdkStarted;
        private bool _connected;
        private bool _disposed;

        public event Action<string> StatusChanged;

        public bool Connected
        {
            get { lock (_sync) return _connected; }
        }

        public DisplayResolution ActiveResolution { get; private set; }

        public NativeDisplay(int preferredWidth, int preferredHeight)
        {
            _preferredWidth = preferredWidth;
            _preferredHeight = preferredHeight;
            _picturePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DisplayPicture)));
        }

        public int Start()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_sdkStarted)
                    return 0;

                int result;
                IntPtr edidPointer = IntPtr.Zero;
                try
                {
                    DisplayEdid edid = new DisplayEdid
                    {
                        Mode = 2,
                        Replacement = string.Empty,
                        AppendCount = 1,
                        AppendedTimings = new DisplayTiming[16]
                    };
                    edid.AppendedTimings[0] = _preferredWidth == 480 && _preferredHeight == 480
                        ? new DisplayTiming
                        {
                            Vic = 143, Polarity = 7, HTotal = 600, VTotal = 490,
                            HActive = 480, VActive = 480, PixelClock = 1764,
                            VerticalFrequency = 6000, HOffset = 100, VOffset = 8,
                            HSyncWidth = 50, VSyncWidth = 4
                        }
                        : new DisplayTiming
                    {
                        Vic = 171, Polarity = 7, HTotal = 488, VTotal = 996,
                        HActive = 376, VActive = 960, PixelClock = 2916,
                        VerticalFrequency = 6000, HOffset = 312, VOffset = 66,
                        HSyncWidth = 112, VSyncWidth = 10
                    };
                    edidPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DisplayEdid)));
                    Marshal.StructureToPtr(edid, edidPointer, false);
                    result = Wrraper_MSDisplayStart(0, edidPointer);
                }
                catch (SEHException exception)
                {
                    Log.Write("SDK start raised SEHException: " + exception);
                    RaiseStatus("SDK 启动失败：原厂程序可能仍在占用设备");
                    return -100;
                }
                finally
                {
                    if (edidPointer != IntPtr.Zero)
                        Marshal.FreeHGlobal(edidPointer);
                }

                _sdkStarted = result == 0;
                if (result == 0)
                {
                    _attachCallback = OnAttach;
                    _detachCallback = OnDetach;
                    Wrraper_MSDisplayRegisterCallback(
                        Marshal.GetFunctionPointerForDelegate(_attachCallback),
                        Marshal.GetFunctionPointerForDelegate(_detachCallback));
                    RaiseStatus("显示 SDK 已启动，正在等待设备…");
                }
                else if (result == -3)
                    RaiseStatus("设备正被 JONSBO-AIO 占用，请从系统托盘退出原厂程序");
                else
                    RaiseStatus("显示 SDK 启动失败，错误码 " + result);
                return result;
            }
        }

        public int Send(Bitmap frame, int rotationDegrees)
        {
            if (frame == null)
                throw new ArgumentNullException("frame");

            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_connected)
                    return -10;
                try
                {
                    int targetWidth;
                    int targetHeight;
                    FrameRotation.GetTargetSize(
                        frame.Width, frame.Height, rotationDegrees,
                        out targetWidth, out targetHeight);
                    if (targetWidth != ActiveResolution.Width || targetHeight != ActiveResolution.Height)
                        return -11;

                    return rotationDegrees == 0
                        ? SendPhysicalFrame(frame)
                        : SendRotatedFrame(frame, rotationDegrees, targetWidth, targetHeight);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return -14;
                }
            }
        }

        private int SendRotatedFrame(Bitmap frame, int rotationDegrees, int targetWidth, int targetHeight)
        {
            Rectangle rectangle = new Rectangle(0, 0, frame.Width, frame.Height);
            BitmapData bitmapData = null;
            try
            {
                bitmapData = frame.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                if (Math.Abs(bitmapData.Stride) != frame.Width * 4)
                    return -12;

                int requiredBytes = checked(targetWidth * targetHeight * 4);
                EnsureRotationBuffer(requiredBytes);
                RotateIntoBuffer(
                    bitmapData.Scan0,
                    bitmapData.Stride,
                    frame.Width,
                    frame.Height,
                    rotationDegrees,
                    _rotationBuffer);
                return SendPhysicalPixels(_rotationBuffer, targetWidth, targetHeight);
            }
            catch (Exception exception)
            {
                Log.Write("Rotated frame send failed: " + exception);
                return -13;
            }
            finally
            {
                if (bitmapData != null)
                    frame.UnlockBits(bitmapData);
            }
        }

        private void EnsureRotationBuffer(int requiredBytes)
        {
            if (_rotationBuffer != IntPtr.Zero && _rotationBufferBytes == requiredBytes)
                return;
            if (_rotationBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(_rotationBuffer);
            _rotationBuffer = Marshal.AllocHGlobal(requiredBytes);
            _rotationBufferBytes = requiredBytes;
        }

        private static unsafe void RotateIntoBuffer(
            IntPtr sourcePointer, int sourceStride,
            int sourceWidth, int sourceHeight, int rotationDegrees,
            IntPtr targetPointer)
        {
            int targetWidth = rotationDegrees == 90 || rotationDegrees == 270
                ? sourceHeight : sourceWidth;
            int* target = (int*)targetPointer.ToPointer();
            byte* sourceBase = (byte*)sourcePointer.ToPointer();
            int positiveStride = Math.Abs(sourceStride);

            for (int sourceY = 0; sourceY < sourceHeight; sourceY++)
            {
                byte* row = sourceStride >= 0
                    ? sourceBase + sourceY * sourceStride
                    : sourceBase + (sourceHeight - 1 - sourceY) * positiveStride;
                int* source = (int*)row;
                for (int sourceX = 0; sourceX < sourceWidth; sourceX++)
                {
                    int targetX;
                    int targetY;
                    if (rotationDegrees == 90)
                    {
                        targetX = sourceHeight - 1 - sourceY;
                        targetY = sourceX;
                    }
                    else if (rotationDegrees == 180)
                    {
                        targetX = sourceWidth - 1 - sourceX;
                        targetY = sourceHeight - 1 - sourceY;
                    }
                    else
                    {
                        targetX = sourceY;
                        targetY = sourceWidth - 1 - sourceX;
                    }
                    target[targetY * targetWidth + targetX] = source[sourceX];
                }
            }
        }

        private int SendPhysicalFrame(Bitmap frame)
        {
                Rectangle rectangle = new Rectangle(0, 0, frame.Width, frame.Height);
                BitmapData bitmapData = null;
                try
                {
                    bitmapData = frame.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    if (Math.Abs(bitmapData.Stride) != frame.Width * 4)
                        return -12;

                    return SendPhysicalPixels(bitmapData.Scan0, frame.Width, frame.Height);
                }
                catch (Exception exception)
                {
                    Log.Write("Frame send failed: " + exception);
                    return -13;
                }
                finally
                {
                    if (bitmapData != null)
                        frame.UnlockBits(bitmapData);
                }
        }

        private int SendPhysicalPixels(IntPtr pixels, int width, int height)
        {
            DisplayPicture picture = new DisplayPicture
            {
                Width = width,
                Height = height,
                Data = pixels
            };
            Marshal.StructureToPtr(picture, _picturePointer, false);
            return SendPictureNative(_handle, _picturePointer, false);
        }

        public void Stop()
        {
            lock (_sync)
            {
                _connected = false;
                if (_sdkStarted)
                {
                    try
                    {
                        Wrraper_MSDisplayStop();
                    }
                    catch (Exception exception)
                    {
                        Log.Write("SDK stop failed: " + exception);
                    }
                }
                _sdkStarted = false;
                RaiseStatus("设备连接已停止");
            }
        }

        private void OnAttach(uint handle, IntPtr resolutions, int count)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                try
                {
                    List<DisplayResolution> modes = new List<DisplayResolution>();
                    int structureSize = Marshal.SizeOf(typeof(DisplayResolution));
                    for (int index = 0; index < count; index++)
                    {
                        IntPtr address = IntPtr.Add(resolutions, index * structureSize);
                        modes.Add((DisplayResolution)Marshal.PtrToStructure(address, typeof(DisplayResolution)));
                    }

                    DisplayResolution selected = new DisplayResolution();
                    bool found = false;
                    foreach (DisplayResolution mode in modes)
                    {
                        if (mode.Width == _preferredWidth && mode.Height == _preferredHeight)
                        {
                            selected = mode;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        foreach (DisplayResolution mode in modes)
                        {
                            if (mode.Width == _preferredHeight && mode.Height == _preferredWidth)
                            {
                                selected = mode;
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
                        RaiseStatus("设备没有报告所需分辨率：" + _preferredWidth + " × " + _preferredHeight);
                        return;
                    }

                    if (_resolutionPointer != IntPtr.Zero)
                        Marshal.FreeHGlobal(_resolutionPointer);
                    _resolutionPointer = Marshal.AllocHGlobal(structureSize);
                    Marshal.StructureToPtr(selected, _resolutionPointer, false);

                    int result = SetVideoParameter(handle, _resolutionPointer);
                    Thread.Sleep(100);
                    _handle = handle;
                    ActiveResolution = selected;
                    _connected = result == 0;

                    string allModes = string.Join(", ", modes.ConvertAll(delegate(DisplayResolution mode)
                    {
                        return mode.ToString();
                    }).ToArray());
                    Log.Write(string.Format("Device attach handle={0}, set={1}, modes={2}", handle, result, allModes));
                    RaiseStatus(_connected
                        ? "已连接乔思伯屏幕：" + selected
                        : "设备已找到，但设置分辨率失败，错误码 " + result);
                }
                catch (Exception exception)
                {
                    Log.Write("Attach callback failed: " + exception);
                    RaiseStatus("设备连接回调失败：" + exception.Message);
                }
            }
        }

        private void OnDetach(uint handle)
        {
            lock (_sync)
            {
                if (handle == _handle)
                {
                    _connected = false;
                    RaiseStatus("乔思伯屏幕已断开");
                }
            }
        }

        private void RaiseStatus(string value)
        {
            Log.Write(value);
            Action<string> handler = StatusChanged;
            if (handler != null)
            {
                try { handler(value); }
                catch { }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("NativeDisplay");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            Stop();
            lock (_sync)
            {
                if (_resolutionPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_resolutionPointer);
                if (_picturePointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_picturePointer);
                if (_rotationBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_rotationBuffer);
                _resolutionPointer = IntPtr.Zero;
                _picturePointer = IntPtr.Zero;
                _rotationBuffer = IntPtr.Zero;
                _rotationBufferBytes = 0;
                _disposed = true;
                GC.KeepAlive(_attachCallback);
                GC.KeepAlive(_detachCallback);
            }
        }
    }
}
