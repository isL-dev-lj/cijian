# 词间

Windows 英语拼写练习工具。包含六级词汇、句子练习和考研大纲词频练习。支持跟打、挖空、记忆和刷词，考研词频库可收藏并在下一组优先回练。

安装包发布后，可从 GitHub Releases 下载 `Cijian-Update.zip`，解压后双击 `Cijian.exe`。管理窗口会显示实际可用的全局快捷键。进度保存在 Windows 用户目录，升级后可以接着练。

## 发布

在 Windows 上用 PowerShell 运行以下命令，传入仓库名和你有权随程序分发的词频表。公开仓库提供源码，构建时需自行提供至少 51 条不同词条。词频表每行为一个单词和一条释义，用制表符分隔，编码为 UTF-8。

```powershell
.\Build-Release.ps1 -Repository 'isL-dev-lj/cijian' -WordList 'C:\path\GraduateFrequency.tsv'
```

构建会把应用内的更新页指向该仓库的最新 Release。将生成的 `dist\Cijian-Update.zip` 上传到与程序集版本对应的 GitHub Release。
