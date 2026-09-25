# WeatherAlert Go（Termux 版）

零依赖单文件二进制，不需要安装任何运行时。中央气象台（nmc.cn）公开预警数据。

## 安装（Termux）

```bash
unzip weather-alert-go.zip
cp weather-alert-go/weather-alert ~/bin/   # 或 $PREFIX/bin
chmod +x ~/bin/weather-alert
weather-alert stat
```

## 用法

```bash
weather-alert list                                        # 最新预警（默认 30 条，按等级排序）
weather-alert list --province 辽宁省 --level 橙色          # 省份+等级筛选
weather-alert list --type 暴雨 --q 深圳 --limit 10        # 类型/关键词
weather-alert detail <预警ID>                             # 详情（正文+防御指南）
weather-alert stat                                        # 全国统计
weather-alert types                                       # 查看可选省份/类型/等级
```

支持彩色等级输出，数据有 5 分钟概念上的实时性（直接读中央气象台接口）。
