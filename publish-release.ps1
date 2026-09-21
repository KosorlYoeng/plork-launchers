# ======================================================================
#  MzzPlork 启动器 - 发布新版本脚本 (Windows PowerShell)
#  用法：powershell -ExecutionPolicy Bypass -File publish-release.ps1
#
#  做什么：
#    1) 读取 .env 里的 GITHUB_USER / REPO / BRAND_VER / EXE_PATH
#    2) 确认 exe 存在
#    3) 生成/追加 changelog.txt 的新版本记录
#    4) 提交 + push 到 GitHub
#    5) 用 gh 创建一个新的 Release (tag=BRAND_VER) 并上传 exe
#    6) 打印 version.json 应该填的 download_url 地址
# ======================================================================

$ErrorActionPreference = "Stop"

function Write-Title($t)  { Write-Host "`n== $t ==" -ForegroundColor Cyan }
function Write-Ok($m)     { Write-Host "[OK]  $m" -ForegroundColor Green }
function Write-Warn($m)   { Write-Host "[!]   $m" -ForegroundColor Yellow }
function Write-Err($m)    { Write-Host "[X]   $m" -ForegroundColor Red }
function Ask($q, $def)    { $r = Read-Host "$q (默认: $def)"; if ([string]::IsNullOrWhiteSpace($r)) { return $def } else { return $r.Trim() } }
function Confirm($q, $d="y") {
    $r = Ask $q $d
    return ($r -match "^[yY]")
}

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $SCRIPT_DIR

# ---------- 依赖检查 ----------
Write-Title "检查依赖"
foreach ($cmd in @("git", "gh")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Err "未安装 $cmd，请先装好（init-github.ps1 里有说明）。"
        exit 1
    }
}
Write-Ok "git / gh 就绪"

# ---------- 解析 .env ----------
Write-Title "读取 .env"
$ENV_FILE = Join-Path $SCRIPT_DIR ".env"
$ENV_TPL  = Join-Path $SCRIPT_DIR ".env.template"
if (-not (Test-Path $ENV_FILE)) {
    if (Test-Path $ENV_TPL) { Copy-Item $ENV_TPL $ENV_FILE; Write-Warn "已从 .env.template 复制生成 .env，请填写后再运行。" }
    else { Write-Err "找不到 .env 也找不到 .env.template。请先运行一次 init-github.ps1。" }
    exit 1
}
function Parse-Env($path) {
    $cfg = @{}
    foreach ($line in Get-Content $path -Encoding UTF8) {
        $l = $line.Trim()
        if ([string]::IsNullOrEmpty($l) -or $l.StartsWith("#")) { continue }
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

function Req($key) {
    if ($cfg.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($cfg[$key]) -and $cfg[$key] -notmatch "^your-") {
        return $cfg[$key]
    }
    Write-Err ".env 中缺少 $key（或者值还是占位符），请手动编辑 .env 填写后重试。"
    exit 1
}

$GITHUB_USER   = Req "GITHUB_USER"
$GITHUB_REPO   = Req "GITHUB_REPO"
$GITHUB_BRANCH = if ($cfg.ContainsKey("GITHUB_BRANCH")   -and -not [string]::IsNullOrWhiteSpace($cfg["GITHUB_BRANCH"])) { $cfg["GITHUB_BRANCH"] }   else { "main" }
$BRAND_FULL    = if ($cfg.ContainsKey("BRAND_FULL")     -and -not [string]::IsNullOrWhiteSpace($cfg["BRAND_FULL"]))   { $cfg["BRAND_FULL"] }     else { "MzzPlork" }
$BRAND_VER     = Req "BRAND_VER"
$EXE_PATH      = if ($cfg.ContainsKey("EXE_PATH")       -and -not [string]::IsNullOrWhiteSpace($cfg["EXE_PATH"]))     { $cfg["EXE_PATH"] }       else { (Join-Path $SCRIPT_DIR "MzzPlork.exe") }

Write-Host "  GITHUB_USER     = $GITHUB_USER"
Write-Host "  GITHUB_REPO     = $GITHUB_REPO"
Write-Host "  GITHUB_BRANCH   = $GITHUB_BRANCH"
Write-Host "  BRAND_FULL      = $BRAND_FULL"
Write-Host "  BRAND_VER       = $BRAND_VER"
Write-Host "  EXE_PATH        = $EXE_PATH"

# ---------- 检查 exe & tag 是否已存在 ----------
Write-Title "检查资源"

if (-not (Test-Path $EXE_PATH)) {
    Write-Err "找不到 $EXE_PATH，请先重新编译 MzzPlork.exe："
    Write-Host "  & `"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`" /target:winexe /out:`"MzzPlork.exe`" /nologo Launcher.cs"
    exit 1
}
$exeSize = (Get-Item $EXE_PATH).Length
Write-Ok "MzzPlork.exe 存在，大小 = $([Math]::Round($exeSize/1KB, 1)) KB"

$tagExists = $false
try {
    gh release view $BRAND_VER --repo "$GITHUB_USER/$GITHUB_REPO" 2>&1 | Out-Null
    $tagExists = ($LASTEXITCODE -eq 0)
} catch { $tagExists = $false }

if ($tagExists) {
    Write-Warn "Release tag $BRAND_VER 已经存在。"
    if (-not (Confirm "是否删除旧 Release 重新发布？(y/N)" "n")) {
        Write-Err "发布中止。把 .env 里的 BRAND_VER 改成一个更大的版本号（如 V2.1 -> V2.2）再运行。"
        exit 1
    }
    Write-Host "  删除旧 Release ..."
    gh release delete $BRAND_VER --repo "$GITHUB_USER/$GITHUB_REPO" -y
    Write-Ok "已删除旧 Release"
}

# ---------- 写 changelog 新条目 ----------
Write-Title "更新 changelog.txt"

$CL_FILE = Join-Path $SCRIPT_DIR "changelog.txt"
$note = Ask "简要写一下这个版本的更新内容（会写入 changelog.txt 和 Release）" "例行更新"

$date = Get-Date -Format "yyyy-MM-dd"
$newEntry = "[{0}] {1}`n  · {2}`n" -f $date, $BRAND_VER, ($note -join " ")

$clContent = if (Test-Path $CL_FILE) { Get-Content $CL_FILE -Raw -Encoding UTF8 } else { "" }
if (-not $clContent.StartsWith("[" + $date + "] " + $BRAND_VER)) {
    # 在最前面插入新版本
    $final = $newEntry + "`n" + $clContent
    Set-Content -Path $CL_FILE -Value $final -Encoding UTF8
    Write-Ok "已在 changelog.txt 顶部追加版本记录"
} else {
    Write-Warn "changelog.txt 顶部已存在相同日期+版本的记录，跳过。"
}

# 同步更新 version.json 本地文件（可选）
$VER_FILE = Join-Path $SCRIPT_DIR "version.json"
$dl = "https://github.com/$GITHUB_USER/$GITHUB_REPO/releases/download/$BRAND_VER/MzzPlork.exe"
$rl = "https://github.com/$GITHUB_USER/$GITHUB_REPO/releases/latest"
$vjson = @"
{
  "version": "$BRAND_VER",
  "download_url": "$dl",
  "release_page": "$rl",
  "note": "$note"
}
"@
Set-Content -Path $VER_FILE -Value $vjson -Encoding UTF8
Write-Ok "本地 version.json 已更新（version=$BRAND_VER）"

# ---------- git commit + push ----------
Write-Title "提交并推送到 GitHub"

git add -A
git commit -m "release: $BRAND_VER  - $note"
if ($LASTEXITCODE -ne 0) { Write-Warn "没有新改动需要提交。" }

git push origin $GITHUB_BRANCH
if ($LASTEXITCODE -ne 0) {
    Write-Err "push 失败。"
    exit 1
}
Write-Ok "已推送到 origin/$GITHUB_BRANCH"

# ---------- 发 Release ----------
Write-Title "发 GitHub Release  tag=$BRAND_VER"

$releaseTitle = "$BRAND_VER  -  $BRAND_FULL"
$noteFile = Join-Path $SCRIPT_DIR ".release-note.md"
@"
# $BRAND_VER

$note

## 文件说明
- **MzzPlork.exe** - 直接双击即可运行（Win10/11 自带 .NET Framework 4）

## 服务器连接
- connect: $($cfg["SERVER_CONNECT"] ?? "已内置在启动器中")
"@ | Set-Content -Path $noteFile -Encoding UTF8

gh release create $BRAND_VER $EXE_PATH --title $releaseTitle --notes-file $noteFile
if ($LASTEXITCODE -ne 0) {
    Write-Err "gh release create 失败，请检查错误输出。"
    Remove-Item $noteFile -ErrorAction SilentlyContinue
    exit 1
}
Remove-Item $noteFile -ErrorAction SilentlyContinue
Write-Ok "Release 发布成功！"

# ---------- 完成输出 ----------
Write-Title "完成！以下是发布结果与下一步"

$releaseURL = "https://github.com/$GITHUB_USER/$GITHUB_REPO/releases/tag/$BRAND_VER"
Write-Host ""
Write-Host " Release 地址：$releaseURL" -ForegroundColor Green
Write-Host ""
Write-Host " ↓↓↓ 下一步请在 GitHub 网页上确认 version.json 内容 ↓↓↓" -ForegroundColor Cyan
Write-Host ""
Write-Host " 仓库里的 version.json 必须长这样（本脚本已尽量自动改好）："
Write-Host "  ------------------------------"
Write-Host "  {`"version`":      `"$BRAND_VER`", "
Write-Host "   `"download_url`": `"$dl`", "
Write-Host "   `"release_page`": `"$rl`", "
Write-Host "   `"note`":         `"$note`"}"
Write-Host "  ------------------------------"
Write-Host ""
Write-Warn "如果启动器仍然提示旧版本，请检查 GitHub 仓库的 version.json 是否和上面一致（必须通过 raw.githubusercontent.com 能访问到正确内容）。"
Write-Host ""
Write-Host "  Raw URL: https://raw.githubusercontent.com/$GITHUB_USER/$GITHUB_REPO/$GITHUB_BRANCH/version.json"
