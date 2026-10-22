param(
 [string]$GameRoot='D:/programs(D)/Steam/steamapps/common/Slay the Spire 2',
 [string]$SettingsPath='C:/Users/31109/AppData/Roaming/SlayTheSpire2/steam/76561199548045370/settings.save'
)
$ErrorActionPreference='Stop'
if(Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue){throw '请保存并退出游戏后安装。'}
$gameFull=[IO.Path]::GetFullPath($GameRoot)
$modsFull=[IO.Path]::GetFullPath((Join-Path $gameFull 'mods'))
$source=Join-Path $PSScriptRoot 'release/AikunMiscPatchPack'
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'AikunMiscPatchPack/AikunMiscPatchPack.dll')){$source=Join-Path $PSScriptRoot 'AikunMiscPatchPack'}
if(!(Test-Path -LiteralPath (Join-Path $source 'AikunMiscPatchPack.dll'))){throw '发行文件不完整。'}
if((Get-Content (Join-Path $gameFull 'release_info.json') -Raw|ConvertFrom-Json).version -ne 'v0.111.0'){throw '游戏版本改变，请先重新检查兼容性。'}
$oldNames=@('AdaptiveTopBar','AutoAnthony_LocalFix','BetterAscension_LocalFix','CardFusion_LocalFix','ColumbinaMod_Beta111_Local','DixieRelicSort_LocalFix','FusionExpansionPack_LocalFix','FusionQuestBlessingFix','Gravity_LocalFix','plague_LocalFix','RelicRewardChoices_LocalFix','YUICore_LocalFix','YuWanCard_LocalFix')
$archive=[IO.Path]::GetFullPath((Join-Path $gameFull ('local-patch-archive/AikunMiscPatchPack-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))))
$plan=@(foreach($name in $oldNames){
 $old=[IO.Path]::GetFullPath((Join-Path $modsFull $name))
 $destination=[IO.Path]::GetFullPath((Join-Path $archive $name))
 if(!$old.StartsWith($modsFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !$destination.StartsWith($archive+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '归档路径越界。'}
 if(Test-Path -LiteralPath $old){[pscustomobject]@{Source=$old;Destination=$destination}}
})
$settings=Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $archive -Force|Out-Null
Copy-Item -LiteralPath $SettingsPath -Destination (Join-Path $archive 'settings-before.save')
$plan | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $archive 'migration-plan.json') -Encoding utf8
foreach($entry in $plan){Move-Item -LiteralPath $entry.Source -Destination $entry.Destination}
$install=Join-Path $modsFull 'AikunMiscPatchPack'
if(Test-Path -LiteralPath $install){
 $previous=Join-Path $archive 'installed-pack-before'
 New-Item -ItemType Directory -Path $previous -Force|Out-Null
 foreach($file in @('AikunMiscPatchPack.dll','AikunMiscPatchPack.json')){
  $current=Join-Path $install $file
  if(Test-Path -LiteralPath $current){Copy-Item -LiteralPath $current -Destination $previous}
 }
}
New-Item -ItemType Directory -Path $install -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $source 'AikunMiscPatchPack.dll'),(Join-Path $source 'AikunMiscPatchPack.json') -Destination $install -Force
# 仅调整模组顺序和移除旧本地补丁项；保留其他设置、原模组启用状态和存档进度。
$formerIds=@('AdaptiveTopBar','FusionQuestBlessingFix','AutoAnthony','BetterAscension','CardFusion','ColumbinaMod','DixieRelicSort','FusionExpansionPack','Gravity','plague','RelicRewardChoices','YUICore','YuWanCard')
$remaining=@($settings.mod_settings.mod_list | Where-Object {$_.id -ne 'AikunMiscPatchPack' -and $_.id -ne 'STS2-RitsuLib' -and !($_.source -eq 'mods_directory' -and $_.id -in $formerIds)})
$settings.mod_settings.mod_list=@([pscustomobject]@{id='STS2-RitsuLib';is_enabled=$true;source='steam_workshop'},[pscustomobject]@{id='AikunMiscPatchPack';is_enabled=$true;source='mods_directory'})+$remaining
[IO.File]::WriteAllText($SettingsPath,($settings|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
Write-Output ('INSTALLED '+$install)
Write-Output ('ARCHIVE '+$archive)
