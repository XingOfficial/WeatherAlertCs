# 天气预警查询（C# 多平台）

同一核心库（`WeatherAlert.Core`：模型 + 中央气象台 nmc.cn 接口），六种平台产物，全部由 GitHub Actions 云端编译。

| 产物 | 技术 | 说明 |
|---|---|---|
| `WeatherAlert-arm64-v8a.apk` | .NET MAUI | Android 8.0+（minSdk 24）arm64 设备 |
| `WeatherAlert-armeabi-v7a.apk` | .NET MAUI | 32 位 ARM 设备（注：经典 armeabi 已被 Android 废弃，提供的是 armeabi-v7a） |
| `WeatherAlert-windows-x64.exe` | Avalonia | Windows 10/11 x64 单文件免安装 |
| `weather-alert-cli_1.0.0_amd64.deb` | 控制台 | Linux CLI：`weather-alert --help` |
| `weather-alert-gui_1.0.0_amd64.deb` | Avalonia | Linux GUI，含桌面菜单项 |
| `WeatherAlert-ios-arm64.ipa` | .NET MAUI | iPhone arm64，**ad-hoc 签名，需自行重签安装**（见下） |

## 功能（全平台一致）

- 全国实时预警列表，按等级（红>橙>黄>蓝）排序，色条/徽章
- 全国统计概览（总数 + 四级数量）
- 筛选：省份 × 预警类型 × 等级；关键词搜索
- 预警详情（正文 + 防御指南）、收藏（持久化）、分享（移动端）、浏览器打开原文

## CI

推送到 `main` 自动触发，产物在 Actions → Artifacts 下载。

## iOS ipa 说明

GitHub Actions 无 Apple 开发者证书，ipa 为 ad-hoc 签名，安装方式：
- 个人证书重签：用 [Sideloadly](https://sideloadly.io/) / AltStore 登录自己的 Apple ID 重签后安装
- 或将仓库中 `src/WeatherAlert.Mobile` 用自己的签名配置在 macOS 上重新 `dotnet publish`

## 项目结构

```
src/
├── WeatherAlert.Core/        # 模型 + API（共享）
├── WeatherAlert.Cli/         # Linux CLI deb
├── WeatherAlert.Gui/         # Windows exe / Linux GUI deb（Avalonia）
└── WeatherAlert.Mobile/      # Android APK / iOS ipa（MAUI）
packaging/                    # deb control / desktop 文件
```
