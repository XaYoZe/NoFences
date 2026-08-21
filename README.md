# NoFences

NoFences 是一个开源的 Windows 桌面图标分区工具。它使用原生 WinForms 和
Windows Shell 接口，在桌面上显示可移动、可缩放、可换主题的图标面板。

![NoFences 运行效果](screenshot.png "NoFences in action")

## 系统要求

- Windows 10 1607（Build 14393）或更高版本，推荐 Windows 11
- x64 或支持 x64 应用兼容层的 Windows 设备
- .NET Framework 4.8

## 安装与使用

可以从 GitHub Releases 下载安装包或免安装 ZIP。ZIP 只是免安装分发形式；
分区配置仍统一保存在 `%LocalAppData%\NoFences`，不会写入程序所在目录。

把文件或文件夹拖到分区中即可添加。拖入用户桌面或公共桌面根目录中的
`.lnk`、`.url`、`.website` 快捷方式时，NoFences 会把快捷方式搬到自己的托管
目录，使桌面图标隐藏；正常退出、移除图标或卸载时会把它恢复到桌面。

普通文件和文件夹只保存路径引用，不会被搬移或删除。

## 数据安全与恢复

分区元数据位于：

```text
%LocalAppData%\NoFences\<分区 GUID>\__fence_metadata.xml
```

托管中的桌面快捷方式位于相同分区目录下的 `items` 子目录。程序不会用同名
文件覆盖桌面现有内容；公共桌面没有写入权限时，会尝试恢复到当前用户桌面。

若程序异常退出，重新启动通常会根据事务记录自动校正状态。也可以在退出
NoFences 后手工执行永久恢复：

```powershell
NoFences.exe --restore-shortcuts
```

恢复失败时请先备份整个 `%LocalAppData%\NoFences` 目录，再到 GitHub Issues
提交错误信息。不要在未备份的情况下手工修改或删除 `items` 中的文件。

## 从源码构建

项目仅支持 Visual Studio/MSBuild，不支持 `dotnet build`：

```powershell
msbuild NoFences.sln /p:Configuration=Release
```

构建目标固定为 x64，目标框架为 .NET Framework 4.8。

## 许可证

项目按 [LICENSE](LICENSE) 中的条款发布。
