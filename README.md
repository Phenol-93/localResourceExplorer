# LocalResourceExplorer

LocalResourceExplorer 是一个 Windows 本地资源登记库 WPF 桌面软件，用于登记、整理和检索本地文件资源。

它只记录文件路径和用户整理信息，不移动、不删除、不重命名原文件。

转载需标明出处

#### ↓↓↓↓↓下载链接↓↓↓↓↓
链接：https://1850521186.share.123pan.cn/123pan/5HR4vd-YxmyH?pwd=s4Xp#
提取码：s4Xp

## 功能特性

- 添加单个或多个文件
- 添加文件夹并递归扫描
- SQLite 本地数据库保存资源记录
- 按路径去重，避免重复导入
- 资源搜索与排序
- 集合管理：创建、重命名、删除、加入、移出
- 标签管理：创建、删除、修改颜色、批量打标签
- 备注编辑、星标、未整理视图
- 打开文件、打开所在文件夹、复制路径
- 音视频时长读取，失败不影响导入
- 丢失文件检测，不自动删除记录
- CSV / JSON 导出
- SQLite 数据库备份与恢复
- 浅色 / 深色主题
- AI 辅助整理建议，必须手动触发并确认后才会写入数据库

## 安全原则

- 不读取文件正文
- 不上传文件本体
- 不移动、不删除、不重命名真实文件
- 从库中移除记录只删除数据库记录，不删除原文件
- AI 只接收文件名、扩展名、大小、修改时间、时长、已有集合名、已有标签名和可选备注
- AI 建议不会自动应用，用户确认后才会写入数据库
- API Key 不写入日志

## 技术栈

- C#
- WPF
- .NET 10
- SQLite
- Dapper
- CommunityToolkit.Mvvm
- Serilog
- TagLibSharp

## 环境要求

- Windows 10/11
- .NET 10 SDK

## 运行项目

```powershell
dotnet run --project .\LocalResourceExplorer.csproj
```

## 构建项目

```powershell
dotnet build .\LocalResourceExplorer.csproj
```

## 发布绿色免安装版

发布 win-x64 自包含版本：

```powershell
dotnet publish .\LocalResourceExplorer.csproj -c Release -r win-x64 --self-contained true -o .\publish\LocalResourceExplorer-win-x64-portable
```

发布完成后，进入输出目录运行：

```powershell
.\LocalResourceExplorer.exe
```

## 数据与日志位置

默认数据库：

```text
%AppData%\LocalResourceExplorer\library.db
```

默认日志目录：

```text
%AppData%\LocalResourceExplorer\logs
```

## 目录结构

```text
Assets/          图标与图片资源
Data/            SQLite schema
Docs/            项目文档
Helpers/         WPF 转换器等辅助类
Models/          简单数据模型
Repositories/    数据库访问层
Services/        扫描、打开文件、AI、媒体信息、备份等业务能力
Themes/          浅色与深色主题
ViewModels/      MVVM ViewModel
Views/           WPF 窗口与弹窗
```


作者：Phenol93
