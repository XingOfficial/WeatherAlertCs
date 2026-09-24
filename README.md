# WeatherAlert

基于中央气象台（nmc.cn）公开数据的天气预警查询工具，一个核心库（`WeatherAlert.Core`）覆盖多个平台。

> 数据来源：中央气象台公开接口，仅供个人参考，请以官方渠道发布的信息为准。

## 支持平台

| 平台 | 技术 | 形态 |
|---|---|---|
| Android 8.0+ | .NET MAUI | APK（arm64-v8a / armeabi-v7a） |
| Windows 10/11 | Avalonia | x64 单文件免安装 exe |
| Linux | Avalonia / 控制台 | GUI deb（含桌面菜单项）/ CLI deb |
| Android 终端（Termux） | 控制台 | deb 包或裸二进制 |
| iOS | .NET MAUI | ipa（ad-hoc 签名，需自行重签） |

## 功能

- 全国实时预警列表，按等级（红 / 橙 / 黄 / 蓝）排序，色条与徽章标识
- 全国统计概览（预警总数及各等级数量）
- 省份 × 预警类型 × 等级组合筛选，支持关键词搜索
- 预警详情：正文内容与防御指南
- 收藏（本地持久化）、分享（移动端）、跳转浏览器查看原文

## 构建

需要 .NET 10 SDK，安装对应 workload 后执行：

```bash
dotnet workload install maui          # 移动端
dotnet publish src/WeatherAlert.Mobile -f net10.0-android -c Release -r android-arm64
dotnet publish src/WeatherAlert.Gui -c Release -r win-x64 --self-contained
dotnet publish src/WeatherAlert.Cli -c Release -r linux-x64 --self-contained
```

推送至 `main` 分支会自动触发 GitHub Actions 构建，全平台产物可在 Actions 的 Artifacts 中下载。

## 项目结构

```
src/
├── WeatherAlert.Core/        预警模型与中央气象台 API（各平台共享）
├── WeatherAlert.Cli/         命令行版本
├── WeatherAlert.Gui/         桌面版本（Windows / Linux）
└── WeatherAlert.Mobile/      移动版本（Android / iOS）
packaging/                    deb 打包与桌面入口文件
```

## License

MIT
