# Third-party notices

## Taskbar-Lyrics

The NetEase native playback event registration in Jonsbo Canvas is based on
Taskbar-Lyrics: https://github.com/mo-jinran/Taskbar-Lyrics

MIT License

Copyright (c) 2025 沫烬染

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## LibSongInfo

LibSongInfo (https://github.com/Steve-xmh/LibSongInfo) was used only as a
protocol reference. Its GPL-3.0 source code is not bundled or copied into
Jonsbo Canvas.

## Bundled vendor components

The following binary components are included to communicate with compatible
hardware. They are not authored by this project and are not relicensed by this
repository:

- `cpuidsdk.dll` — CPUID SDK binary, digitally signed by CPUID and used for
  hardware sensor access. Copyright and licensing remain with CPUID.
- `MSDISPLAYSDKWRRAPER.dll` — USB display SDK binary, digitally signed by
  Hailian Zhixin (Shenzhen) Technology Co., Ltd. Copyright and licensing remain
  with its owner.
- `drivers/MSUSBDisplay/*` and `libusb0.dll` — the device's signed Windows
  driver package and libusb-win32 runtime. Copyright and licensing remain with
  their respective owners.

These files are provided solely for interoperability with the supported JONSBO
display. Their presence does not grant permission to reuse or redistribute them
outside the terms supplied by their respective owners. No project-level open
source license has been selected for Jonsbo Canvas as of v1.1.0.
