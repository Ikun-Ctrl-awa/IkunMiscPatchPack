param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$SettingsPath)
$ErrorActionPreference='Stop'
if(Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue){throw '请保存并退出游戏后安装。'}
$gameFull=[IO.Path]::GetFullPath($GameRoot)
$modsFull=[IO.Path]::GetFullPath((Join-Path $gameFull 'mods'))
$settingsFull=[IO.Path]::GetFullPath($SettingsPath)
$source=Join-Path $PSScriptRoot 'release/IkunMiscPatchPack'
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'IkunMiscPatchPack/IkunMiscPatchPack.dll')){$source=Join-Path $PSScriptRoot 'IkunMiscPatchPack'}
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'IkunMiscPatchPack.dll')){$source=$PSScriptRoot}
foreach($file in @('IkunMiscPatchPack.dll','IkunMiscPatchPack.json')){if(!(Test-Path -LiteralPath (Join-Path $source $file))){throw ('发行文件不完整：'+$file)}}
if((Get-Content -LiteralPath (Join-Path $gameFull 'release_info.json') -Raw|ConvertFrom-Json).version -ne 'v0.111.0'){throw '游戏版本改变，请先重新检查兼容性。'}
$settings=Get-Content -LiteralPath $settingsFull -Raw|ConvertFrom-Json
$oldNames=@('AikunMiscPatchPack','IkunMiscPatchPack','AdaptiveTopBar','AutoAnthony_LocalFix','BetterAscension_LocalFix','CardFusion_LocalFix','ColumbinaMod_Beta111_Local','DixieRelicSort_LocalFix','FusionExpansionPack_LocalFix','FusionQuestBlessingFix','Gravity_LocalFix','plague_LocalFix','RelicRewardChoices_LocalFix','YUICore_LocalFix','YuWanCard_LocalFix')
$archive=[IO.Path]::GetFullPath((Join-Path $gameFull ('local-patch-archive/IkunMiscPatchPack-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))))
$plan=@(foreach($name in $oldNames){
 $old=[IO.Path]::GetFullPath((Join-Path $modsFull $name));$target=[IO.Path]::GetFullPath((Join-Path $archive $name))
 if(!$old.StartsWith($modsFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !$target.StartsWith($archive+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '安装归档路径越界。'}
 if(Test-Path -LiteralPath $old){[pscustomobject]@{Source=$old;Destination=$target}}
})
New-Item -ItemType Directory -Path $archive -Force|Out-Null
Copy-Item -LiteralPath $settingsFull -Destination (Join-Path $archive 'settings-before.save')
$account=[IO.Path]::GetDirectoryName($settingsFull)
# 改名迁移前保留旧对局及模组数据，不改写存档内容。
foreach($scope in @('', 'modded')){
 $scopeRoot=if($scope){Join-Path $account $scope}else{$account}
 if(!(Test-Path -LiteralPath $scopeRoot)){continue}
 foreach($profile in Get-ChildItem -LiteralPath $scopeRoot -Directory -Filter 'profile*'){
  $saves=Join-Path $profile.FullName 'saves'
  if(Test-Path -LiteralPath $saves){
   $relative=if($scope){Join-Path $scope $profile.Name}else{$profile.Name}
   $backup=Join-Path (Join-Path $archive 'player-saves') $relative
   New-Item -ItemType Directory -Path $backup -Force|Out-Null
   Copy-Item -LiteralPath $saves -Destination $backup -Recurse
  }
 }
}
$oldData=Join-Path $account 'mod_data/AikunMiscPatchPack'
if(Test-Path -LiteralPath $oldData){Copy-Item -LiteralPath $oldData -Destination (Join-Path $archive 'old-mod-data') -Recurse}
$plan|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $archive 'migration-plan.json') -Encoding utf8
foreach($entry in $plan){Move-Item -LiteralPath $entry.Source -Destination $entry.Destination}
$install=Join-Path $modsFull 'IkunMiscPatchPack'
New-Item -ItemType Directory -Path $install -Force|Out-Null
foreach($file in @('IkunMiscPatchPack.dll','IkunMiscPatchPack.json','README.md')){if(Test-Path -LiteralPath (Join-Path $source $file)){Copy-Item -LiteralPath (Join-Path $source $file) -Destination $install}}
$formerIds=@('AikunMiscPatchPack','IkunMiscPatchPack','AdaptiveTopBar','FusionQuestBlessingFix','AutoAnthony','BetterAscension','CardFusion','ColumbinaMod','DixieRelicSort','FusionExpansionPack','Gravity','plague','RelicRewardChoices','YUICore','YuWanCard')
$remaining=@($settings.mod_settings.mod_list|Where-Object {$_.id -ne 'AikunMiscPatchPack' -and $_.id -ne 'IkunMiscPatchPack' -and $_.id -ne 'STS2-RitsuLib' -and !($_.source -eq 'mods_directory' -and $_.id -in $formerIds)})
$oldWorkshop=@($settings.mod_settings.mod_list|Where-Object {$_.id -eq 'AikunMiscPatchPack' -and $_.source -eq 'steam_workshop'}|ForEach-Object {[pscustomobject]@{id=$_.id;is_enabled=$false;source=$_.source}})
$settings.mod_settings.mod_list=@([pscustomobject]@{id='STS2-RitsuLib';is_enabled=$true;source='steam_workshop'},[pscustomobject]@{id='IkunMiscPatchPack';is_enabled=$true;source='mods_directory'})+$oldWorkshop+$remaining
[IO.File]::WriteAllText($settingsFull,($settings|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
Write-Output ('INSTALLED '+$install)
Write-Output ('ARCHIVE '+$archive)
