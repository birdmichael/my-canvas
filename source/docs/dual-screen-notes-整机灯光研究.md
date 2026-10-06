# My Canvas 整机灯光研究

目标仅为机箱风扇、水冷、灯条的灯光，不包含显卡控制。检查日期：2026-10-04。

## 已确认

- 本机主板：GIGABYTE X870 EAGLE WIFI7；GCC / RGB Fusion 已安装。
- 本机枚举到 VID 048D / PID 5711 的 USB 控制器。
- OpenRGB 上游检测源码注册了 PID 5711，并在设备表中明确列出 X870 EAGLE WIFI7。
- GCC 的主板配置包含颜色、灯效、亮度、速度及分区参数。

这些证据支持继续采用 OpenRGB 的主板灯光控制路线，但尚未实机验证灯光写入，也未确认各灯具接线。

## 建议实现路线

My Canvas → 本地 OpenRGB SDK → 技嘉主板 ARGB 分区 → 风扇、水冷、灯条。

先验证主板灯光设备枚举和分区，再测试单色、亮度、呼吸、渐变等效果。只选择主板及已确认的灯光控制器，不选择显卡设备。GCC 与 OpenRGB 需要协调控制权，避免同时修改同一控制器。

连接主板兼容灯光接口的灯具通常可随接口统一控制。分线器上的灯具可能只能作为同一区同步；独立控制盒是否可用取决于型号和协议。软件检测不能证明实际接线。

## AorusLcd 的适用范围

[AorusLcd](https://github.com/CodeTorchAI/AorusLcd) 使用 NVAPI / GPU I2C 控制 AORUS 显卡 LCD 和显卡灯光。README 明确表示不提供整机 RGB 同步。因此不能作为本项目整机灯光的直接驱动；灯效模型、总线节流和互斥结构可作为参考。目前未导入其代码、未执行显卡探测或控制。

## 参考源码

- [OpenRGB 技嘉 USB 灯光控制器](https://github.com/CalcProgrammer1/OpenRGB/tree/master/Controllers/GigabyteRGBFusion2USBController)
- [OpenRGB](https://github.com/CalcProgrammer1/OpenRGB)

## 当前双屏版本

My Canvas 1.2.1-dual.3 已更新原生尺寸硬件布局；保留硬件、音乐及自动切换入口，移除 Codex / Claude 页面与后台额度采集。自动模式在音乐正在播放时显示音乐，暂停或不可用时显示硬件。方屏独立保留时钟选项。

构建、发布成功；27 项检查及网易云桥接回归通过。运行日志确认两块屏幕分别发送 1920×462 和 480×480 帧。灯光功能仍为研究阶段。

硬件预览 PNG 使用测试数据，仅供版式审阅。实际程序读取本机传感器；获取不到温度时显示占位符，不伪造读数。
