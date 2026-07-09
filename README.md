# 苏大校园网防掉线自动登录

苏州大学校园网 Dr.COM 认证系统自动登录工具，基于 WPF 开发。

## 功能

- 断线自动检测与重连
- 多账号管理与切换
- 系统托盘最小化运行
- 开机自启
- 移动热点自动开启
- 高清校徽图标

## 编译

需要 .NET Framework 4.8，使用 csc.exe 编译：

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /target:winexe /out:CampusNet.exe `
  /win32icon:app.ico /resource:suda-logo.png `
  /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" `
  /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" `
  /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" `
  /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" `
  /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" `
  /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll" `
  /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.dll" `
  CampusNetWpf.cs
```

## 原理

通过访问网关 `http://10.9.1.3/?isReback=1` 检测登录状态，提取 `uid='...'` 判断是否在线。断线时自动 POST 到认证接口完成登录。

## License

MIT