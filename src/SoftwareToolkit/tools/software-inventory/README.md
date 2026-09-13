# 软件清单

[返回项目主页](https://github.com/PN-BUG/software-toolkit)

软件清单用于扫描本机已安装的软件，挑选需要保留或分享的条目，并维护对应的下载入口。

## 数据来源

- 当前用户和本机的 32 位、64 位卸载注册表项。
- 当前用户的 Microsoft Store / MSIX 应用。
- WinGet 包匹配结果（系统安装 WinGet 时）。

Store 或 WinGet 不可用时，工具仍会保留从注册表获得的结果。

## 使用方法

1. 打开“软件清单”，等待设备软件扫描完成。
2. 搜索或筛选软件，并将需要的条目加入“我的清单”。
3. 检查自动生成的下载地址，必要时直接修改。
4. 使用“复制分享文本”生成 Markdown，或导出独立 HTML 分享页。

自动下载地址会优先利用注册表官网、Microsoft Store 或匹配到的 WinGet 包 ID。修改过的地址可以恢复为自动值。

## 数据与隐私

清单保存在 `%LOCALAPPDATA%\SoftwareToolkit\software-list.json`。

导出的分享页不会包含本机安装路径等设备隐私信息。分享前仍建议检查软件名称、版本及自定义链接是否适合公开。
