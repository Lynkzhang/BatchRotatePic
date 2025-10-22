# BatchRotatePic

用于批量旋转图片的工具集合。

## WPF 可视化批量旋转工具

`BatchRotateWpf` 目录下提供了一个基于 WPF 的桌面应用，支持以可视化方式批量旋转图片。

主要特性：

- 选择输入与输出文件夹，自动载入常见格式的图片（JPG、PNG、BMP、GIF、TIFF）。
- 预览图片缩略图与大图详情，包含文件尺寸与分辨率信息。
- 自定义旋转角度（限定为 90° 的倍数），并支持配置输出文件名后缀、是否覆盖已存在文件。
- 显示处理进度，可在批量任务执行过程中取消操作。

要运行该应用，请在 Windows 环境下使用 Visual Studio 或 `dotnet` 打开 `BatchRotateWpf/BatchRotateWpf.csproj`（或解决方案）并执行。

## 传统命令行脚本

保留了 `RotatePic/Rotate.bat`，依赖 `NConvert` 命令行工具完成批量旋转操作。
