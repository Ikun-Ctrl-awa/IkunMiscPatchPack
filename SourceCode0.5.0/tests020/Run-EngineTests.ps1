param([ValidateSet('targeted','regression')][string]$Suite='targeted',[string]$Profile='current',[ValidateSet('auto','full','minimal')][string]$Mode='auto')
python -X utf8 "$PSScriptRoot/test_plan.py" --suite $Suite --profile $Profile --mode $Mode
throw '公开源码不含私人游戏宿主。请按README准备物理副本、独立用户目录与请求，再加载编译后的探针；不得将编译结果当作原生回归。'
