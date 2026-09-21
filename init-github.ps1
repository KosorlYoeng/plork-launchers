# ======================================================================
#  MzzPlork 启动器 - GitHub 仓库初始化 & 发布脚本 (Windows PowerShell)
#  用法：右键 -> 使用 PowerShell 运行   或   powershell -ExecutionPolicy Bypass -File init-github.ps1
# ======================================================================
#
# 这个脚本会帮你：
#   1) 检查是否安装了 Git / GitHub CLI (gh)，没装会提示怎么装
#   2) 读取 .env 配置（从 .env.template 复制一份改）
#   3) 生成默认的 changelog.txt / version.json / README.md
#   4) 使用 gh 在你的 GitHub 账号下创建 Public 仓库
#   5) 把所有文件第一次 commit + push 到 GitHub
#   6) （可选）发布第一个 Release，把 MzzPlork.exe 传上去
#
# ======================================================================

$ErrorActionPreference = "Stop"

function Write-Title($t)  { Write-Host "`n== $t ==" -ForegroundColor Cyan }
function Write-Ok($m)     { Write-Host "[OK]  $m" -ForegroundColor Green }
function Write-Warn($m)   { Write-Host "[!]   $m" -ForegroundColor Yellow }
function Write-Err($m)    { Write-Host "[X]   $m" -ForegroundColor Red }
function Ask($q, $def)    { $r = Read-Host "$q (默认: $def)"; if ([string]::IsNullOrWhiteSpace($r)) { return $def } else { return $r.Trim() } }

# ---------- 0. 路径 ----------
$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $SCRIPT_DIR

# ---------- 1. 检查依赖 ----------
Write-Title "检查依赖 (Git / GitHub CLI)"

function Test-CommandExists($name) {
    return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

$git_ok = Test-CommandExists "git"
if ($git_ok) {
    $v = (git --version)
    Write-Ok "已安装 Git  ( $v )"
} else {
    Write-Err "未安装 Git。"
    Write-Warn "请打开 https://git-scm.com/download/win 下载安装后再运行本脚本。"
    exit 1
}

$gh_ok = Test-CommandExists "gh"
if ($gh_ok) {
    $v = (gh --version)
    Write-Ok "已安装 GitHub CLI ( gh )"
} else {
    Write-Err "未安装 GitHub CLI (gh 命令)。"
    Write-Warn "安装方式 2 选 1："
    Write-Warn "  ① 打开 PowerShell 管理员模式，执行：  winget install --id GitHub.cli -e"
    Write-Warn "  ② 手动下载安装： https://cli.github.com/"
    Write-Warn "安装后打开新的 PowerShell 再运行本脚本。"
    exit 1
}

# ---------- 2. gh 登录状态检查 ----------
Write-Title "检查 GitHub 登录状态"
$gh_auth = $false
try {
    gh auth status 2>&1 | Out-Null
    $gh_auth = ($LASTEXITCODE -eq 0)
} catch { $gh_auth = $false }

if (-not $gh_auth) {
    Write-Warn "还没登录 GitHub。 现在运行： gh auth login "
    Write-Warn "推荐选项："
    Write-Warn "  · What account do you want to log into?    -> GitHub.com"
    Write-Warn "  · Preferred protocol for Git operations?   -> HTTPS"
    Write-Warn "  · Authenticate Git with your GitHub credentials? -> Y"
    Write-Warn "  · How would you like to authenticate?      -> Login with a web browser"
    Write-Warn ""
    Write-Warn "按回车打开 gh auth login（浏览器点授权即可）..."
    Read-Host
    gh auth login
    if ($LASTEXITCODE -ne 0) {
        Write-Err "GitHub 登录失败，请手动执行 gh auth login 成功后重试。"
        exit 1
    }
}
Write-Ok "GitHub 已登录。"

# ---------- 3. 读取 .env 配置 ----------
Write-Title "读取配置 (.env)"

$ENV_FILE  = Join-Path $SCRIPT_DIR ".env"
$ENV_TPL   = Join-Path $SCRIPT_DIR ".env.template"

# .env 不存在但 template 存在 -> 复制一份 + 提示填
if ((-not (Test-Path $ENV_FILE)) -and (Test-Path $ENV_TPL)) {
    Copy-Item $ENV_TPL $ENV_FILE
    Write-Warn "已根据 .env.template 生成 .env 文件，请填写下面的配置（直接回车用括号里的默认值）："
}

# 简易 .env 解析（支持 #注释、空值、引号包裹）
function Parse-Env($path) {
    $cfg = @{}
    if (-not (Test-Path $path)) { return $cfg }
    foreach ($line in Get-Content $path -Encoding UTF8) {
        $l = $line.Trim()
        if ([string]::IsNullOrEmpty($l)) { continue }
        if ($l.StartsWith("#")) { continue }
        $idx = $l.IndexOf('=')
        if ($idx -le 0) { continue }
        $k = $l.Substring(0, $idx).Trim()
        $v = $l.Substring($idx + 1).Trim()
        if (($v.StartsWith('"') -and $v.EndsWith('"')) -or ($v.StartsWith("'") -and $v.EndsWith("'"))) {
            $v = $v.Substring(1, $v.Length - 2)
        }
        if (-not [string]::IsNullOrEmpty($k)) { $cfg[$k] = $v }
    }
    return $cfg
}

$cfg = Parse-Env $ENV_FILE

# 交互填必须的字段
function Get-ConfigOrAsk($key, $question, $default) {
    if ($cfg.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($cfg[$key])) {
        Write-Host "  $question  = $($cfg[$key])" -ForegroundColor DarkGray
        return $cfg[$key]
    }
    $val = Ask $question $default
    $cfg[$key] = $val
    return $val
}

Write-Host ""
$GITHUB_USER     = Get-ConfigOrAsk "GITHUB_USER"     "GitHub 用户名（你的账号）"      ""
$GITHUB_REPO     = Get-ConfigOrAsk "GITHUB_REPO"     "仓库名"                         "fivem-launcher"
$GITHUB_BRANCH   = Get-ConfigOrAsk "GITHUB_BRANCH"   "分支名"                         "main"
$REPO_VISIBILITY = Get-ConfigOrAsk "REPO_VISIBILITY" "仓库可见性 (public / private)"  "public"
$BRAND_FULL      = Get-ConfigOrAsk "BRAND_FULL"      "启动器品牌名"                   "MzzPlork"
$BRAND_VER       = Get-ConfigOrAsk "BRAND_VER"       "启动器版本号"                   "V2.0"
$SERVER_CONNECT  = Get-ConfigOrAsk "SERVER_CONNECT"  "FiveM 直连码"                   "6j44p8"
$EXE_PATH        = Get-ConfigOrAsk "EXE_PATH"        "MzzPlork.exe 路径（发布Release 用）"  (Join-Path $SCRIPT_DIR "MzzPlork.exe")

# 关键字段校验
if ([string]::IsNullOrWhiteSpace($GITHUB_USER) -or $GITHUB_USER -match "your-") {
    Write-Err "GitHub 用户名无效，必须是你真实的 GitHub 账号。"
    exit 1
}

# 写回 .env（保存本次输入的值）
function Save-Env($path, $config) {
    $lines = New-Object System.Collections.Generic.List[string]
    if (Test-Path $path) {
        # 先按原有行遍历，值存在就用新值覆盖
        $seen = @{}
        foreach ($line in Get-Content $path -Encoding UTF8) {
            $l = $line.TrimEnd()
            $trim = $l.TrimStart()
            if ([string]::IsNullOrEmpty($trim) -or $trim.StartsWith("#")) { $lines.Add($l); continue }
            $idx = $l.IndexOf('=')
            if ($idx -le 0) { $lines.Add($l); continue }
            $k = $l.Substring(0, $idx).Trim()
            if ($config.ContainsKey($k) -and -not $seen.ContainsKey($k)) {
                $lines.Add("$k=$($config[$k])")
                $seen[$k] = $true
            } else {
                $lines.Add($l)
            }
        }
        # 追加缺失的字段
        foreach ($kv in $config.GetEnumerator()) {
            if (-not $seen.ContainsKey($kv.Key)) { $lines.Add("$($kv.Key)=$($kv.Value)") }
        }
    } else {
        foreach ($kv in $config.GetEnumerator()) { $lines.Add("$($kv.Key)=$($kv.Value)") }
    }
    Set-Content -Path $path -Value $lines -Encoding UTF8
}
Save-Env $ENV_FILE $cfg
Write-Ok "配置已保存到 .env"

# ---------- 4. 生成默认文件 ----------
Write-Title "生成仓库默认文件 (changelog.txt / version.json / README.md / .gitignore)"

$CL_FILE = Join-Path $SCRIPT_DIR "changelog.txt"
if (-not (Test-Path $CL_FILE)) {
@"
[$(Get-Date -Format 'yyyy-MM-dd')] $BRAND_VER  正式版发布
  · 全新极简 UI
  · 修复 FiveM 启动 Shell/Browser 报错
  · 接入 GitHub 自动更新 + 远程更新日志
  · 新增 4 个可切换页面（启动面板 / 更新日志 / 服务状态 / 社群入口）
  · 关闭启动器自动关闭 FiveM
"@ | Set-Content -Path $CL_FILE -Encoding UTF8
    Write-Ok "已创建 changelog.txt"
} else { Write-Host "  changelog.txt 已存在，跳过。" -ForegroundColor DarkGray }

$VER_FILE = Join-Path $SCRIPT_DIR "version.json"
if (-not (Test-Path $VER_FILE)) {
    $download = "https://github.com/$GITHUB_USER/$GITHUB_REPO/releases/download/$BRAND_VER/MzzPlork.exe"
    $release  = "https://github.com/$GITHUB_USER/$GITHUB_REPO/releases/latest"
    @"
{
  "version": "$BRAND_VER",
  "download_url": "$download",
  "release_page": "$release",
  "note": "$BRAND_FULL 启动器 $BRAND_VER 正式发布。"
}
"@ | Set-Content -Path $VER_FILE -Encoding UTF8
    Write-Ok "已创建 version.json"
} else { Write-Host "  version.json 已存在，跳过。" -ForegroundColor DarkGray }

$README = Join-Path $SCRIPT_DIR "README.md"
if (-not (Test-Path $README)) {
    $rawChangelog = "https://raw.githubusercontent.com/$GITHUB_USER/$GITHUB_REPO/$GITHUB_BRANCH/changelog.txt"
    $rawVersion   = "https://raw.githubusercontent.com/$GITHUB_USER/$GITHUB_REPO/$GITHUB_BRANCH/version.json"
@"
# $BRAND_FULL  FiveM 启动器

> 面向 FiveM 玩家的一键启动器：自动识别 FiveM 安装位置 → 自动连接服务器（connect $SERVER_CONNECT）。
> 关闭启动器会自动关闭 FiveM。

## 功能特性

- 🎨 极简 UI（秋叶暖色调），顶部只有最小化和关闭按钮
- 🎮 启动游戏自动加入服务器：connect $SERVER_CONNECT
- 📄 支持 GitHub **远程更新日志**（$rawChangelog）
- ⬆️ 支持 GitHub **自动更新检查**（读取 $rawVersion，版本号更大就弹窗提示下载）
- 🟢 服务状态页：延迟探测、连接状态、复制直连码
- 🎫 社群入口页：QQ群 / Discord / 官网 / 微信号
- 🛑 关闭启动器自动关闭 FiveM 全部进程

## 给管理员（发布新版本）

```powershell
# 1) 修改 Launcher.cs 里的 BRAND_VER 版本号，重新编译
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:"MzzPlork.exe" /nologo Launcher.cs

# 2) 运行本仓库 publish-release.ps1（或手动去 GitHub 发 Release + 上传 exe）
.\publish-release.ps1

# 3) 去 GitHub 编辑仓库里的 version.json，把 version / download_url 更新成新版
```

## 给玩家

直接双击「MzzPlork.exe」即可，不需要安装。如果提示没安装 FiveM，请访问 https://fivem.net 下载安装。
"@ | Set-Content -Path $README -Encoding UTF8
    Write-Ok "已创建 README.md"
} else { Write-Host "  README.md 已存在，跳过。" -ForegroundColor DarkGray }

$GI_FILE = Join-Path $SCRIPT_DIR ".gitignore"
if (-not (Test-Path $GI_FILE)) {
@"
# ==== Windows ====
Thumbs.db
Desktop.ini
$RECYCLE.BIN/
*.lnk

# ==== 编译中间产物 ====
*.pdb
*.obj
bin/
obj/
.vs/

# ==== 本地配置/敏感 ====
.env
*.bak
!Launcher.cs
"@ | Set-Content -Path $GI_FILE -Encoding UTF8
    Write-Ok "已创建 .gitignore"
} else { Write-Host "  .gitignore 已存在，跳过。" -ForegroundColor DarkGray }

# ---------- 5. 创建 GitHub 仓库 ----------
Write-Title "创建 GitHub 仓库  $GITHUB_USER/$GITHUB_REPO  ($REPO_VISIBILITY)"

# 先查仓库是否已存在
$repoExists = $false
try {
    gh repo view "$GITHUB_USER/$GITHUB_REPO" --json name 2>&1 | Out-Null
    $repoExists = ($LASTEXITCODE -eq 0)
} catch { $repoExists = $false }

if ($repoExists) {
    Write-Warn "仓库 $GITHUB_USER/$GITHUB_REPO 已经存在，跳过创建。"
} else {
    $desc = "$BRAND_FULL FiveM 启动器 (connect $SERVER_CONNECT)"
    Write-Host "  执行：gh repo create $GITHUB_USER/$GITHUB_REPO --$REPO_VISIBILITY -d ""$desc"""
    gh repo create "$GITHUB_USER/$GITHUB_REPO" "--$REPO_VISIBILITY" -d $desc
    if ($LASTEXITCODE -ne 0) {
        Write-Err "创建仓库失败，请检查上面的错误信息。"
        exit 1
    }
    Write-Ok "仓库创建成功。"
}

# ---------- 6. 本地初始化 Git + 提交 + push ----------
Write-Title "初始化 Git 并推送"

$REMOTE_URL = "https://github.com/$GITHUB_USER/$GITHUB_REPO.git"

if (-not (Test-Path (Join-Path $SCRIPT_DIR ".git"))) {
    git init -b $GITHUB_BRANCH
    if ($LASTEXITCODE -ne 0) { Write-Err "git init 失败"; exit 1 }
    Write-Ok "已初始化 git 仓库 (分支：$GITHUB_BRANCH)"
} else {
    Write-Ok "当前目录已是 git 仓库。"
}

# 配置 git 用户（如果没配置）
try {
    $cu = git config user.name 2>$null
    $ce = git config user.email 2>$null
    if ([string]::IsNullOrWhiteSpace($cu)) {
        $n = Ask "git user.name (显示名)"  $GITHUB_USER
        git config user.name $n
    }
    if ([string]::IsNullOrWhiteSpace($ce)) {
        $e = Ask "git user.email (邮箱)"   "$GITHUB_USER@users.noreply.github.com"
        git config user.email $e
    }
} catch { }

# 加 remote
$hasRemote = $false
try {
    $rm = git remote -v
    if ($rm -match "github") { $hasRemote = $true }
} catch {}
if (-not $hasRemote) {
    git remote add origin $REMOTE_URL
    Write-Ok "已添加 remote origin = $REMOTE_URL"
} else {
    Write-Host "  remote origin 已存在，跳过。" -ForegroundColor DarkGray
}

git add -A
git commit -m "init: $BRAND_VER 初始化仓库（更新日志、版本信息、README）"
if ($LASTEXITCODE -ne 0) {
    Write-Warn "没有新的改动需要提交。"
}

# push
Write-Host "  推送到 GitHub origin/$GITHUB_BRANCH ..." -ForegroundColor Cyan
git push -u origin $GITHUB_BRANCH
if ($LASTEXITCODE -ne 0) {
    Write-Err "push 失败。可能原因："
    Write-Err "  · 分支名不对（如果远端默认是 master，但你用了 main？）"
    Write-Err "  · gh 登录态失效，重新跑 gh auth login"
    Write-Err "  · 仓库名冲突或权限不足"
    exit 1
}
Write-Ok "已成功推送到 GitHub：$REMOTE_URL"

# ---------- 7. 可选：发第一个 Release ----------
Write-Title "发布第一个 Release（可选）"

$exeOk = Test-Path $EXE_PATH
if ($exeOk) {
    $size = (Get-Item $EXE_PATH).Length
    $doRelease = Ask "检测到 MzzPlork.exe（$([Math]::Round($size/1KB,1)) KB），是否立即发布 Release $BRAND_VER 并上传？(y/N)" "n"
    if ($doRelease -match "^[yY]") {
        $tag = $BRAND_VER
        $title = "$BRAND_VER  - $BRAND_FULL"
        $note_file = Join-Path $SCRIPT_DIR ".release-note.md"
        @"
# $BRAND_VER

$BRAND_FULL FiveM 启动器首发版本。

- 一键连接服务器：connect $SERVER_CONNECT
- 自动更新检查
- 远程更新日志
"@ | Set-Content -Path $note_file -Encoding UTF8

        Write-Host "  创建 Release tag=$tag ..." -ForegroundColor Cyan
        gh release create $tag $EXE_PATH --title $title --notes-file $note_file
        if ($LASTEXITCODE -ne 0) {
            Write-Err "gh release create 失败，你也可以手动去 GitHub 网页发布 Release。"
        } else {
            Write-Ok "Release 已发布：https://github.com/$GITHUB_USER/$GITHUB_REPO/releases/tag/$tag"
            Write-Warn "记得去 GitHub 编辑仓库里的 version.json，把 download_url / release_page / version 对应当前 Release 地址填好。"
        }
        Remove-Item $note_file -ErrorAction SilentlyContinue
    } else {
        Write-Host "  跳过发布 Release。" -ForegroundColor DarkGray
    }
} else {
    Write-Warn "没找到 MzzPlork.exe（路径：$EXE_PATH），无法发布 Release。"
    Write-Warn "先编译：& `"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`" /target:winexe /out:`"MzzPlork.exe`" /nologo Launcher.cs"
}

# ---------- 8. 汇总输出 ----------
Write-Title "完成！"
Write-Ok "GitHub 仓库地址：https://github.com/$GITHUB_USER/$GITHUB_REPO"
Write-Host ""
Write-Host "【Launcher.cs 你要改的配置（对应 .env 里你填的信息）】" -ForegroundColor Cyan
Write-Host "  GITHUB_USER = ""$GITHUB_USER"""
Write-Host "  GITHUB_REPO = ""$GITHUB_REPO"""
Write-Host "  GITHUB_BRANCH = ""$GITHUB_BRANCH"""
Write-Host "  SERVER_CONNECT_CODE = ""$SERVER_CONNECT"""
Write-Host "  BRAND_FULL = ""$BRAND_FULL"""
Write-Host "  BRAND_VER  = ""$BRAND_VER"""
Write-Host ""
Write-Host "【发布新版本的顺序】以后重复走 3 步：" -ForegroundColor Cyan
Write-Host "  1. Launcher.cs 改 BRAND_VER  +  重新编译 exe"
Write-Host "  2. 运行  .\publish-release.ps1 （自动发 Release 并上传 exe）"
Write-Host "  3. 去 GitHub 网页编辑仓库里的 version.json：version / download_url 改成新版本"
Write-Host ""
Write-Host "  或者不用脚本，直接在 GitHub 网页 → Releases → Draft a new release 手动发布也行。"
