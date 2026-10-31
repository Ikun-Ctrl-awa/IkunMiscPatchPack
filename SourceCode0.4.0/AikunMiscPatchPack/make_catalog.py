"""建立原件校验清单与压缩差分；发行包不包含第三方原始资源包。"""
from pathlib import Path
import hashlib,json,subprocess,os
ROOT=Path(os.environ['STS2_PATCH_INPUTS_DIR']);H=Path(os.environ['STS2_WORKSPACE_DIR']);W=Path(os.environ['STS2_WORKSHOP_DIR'])
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
targets=[
 ('ColumbinaMod','3781489058/ColumbinaMod.dll',['columbina_api','moon_gleam'],H/'ColumbinaRepair/original/ColumbinaMod.dll'),
 ('BetterAscension','3803147446/BetterAscension.dll',['bane_ethereal','act_attrition'],H/'BetterAscensionRepair/original/BetterAscension.dll'),
 ('FusionExpansionPack','3762574884/FusionExpansionPack.dll',['fusion_rewards','super_disassembly'],H/'WorkshopCompatRepair/original/3762574884/FusionExpansionPack.dll'),
 ('YUICore','3747562679/YUICore.dll',['yui_replay','yui_choice'],H/'CombatSettlementRepair/original/YUICore.dll'),
 ('AutoAnthony','3786611028/AutoAnthony.dll',['anthony_pool'],H/'WorkshopCompatRepair/original/3786611028/AutoAnthony.dll'),
 ('Gravity','3802891146/Gravity.dll',['gravity_toggle'],H/'WorkshopCompatRepair/original/3802891146/Gravity.dll'),
 ('RelicRewardChoices','3795496596/RelicRewardChoices/RelicRewardChoices.dll',['chest_key'],H/'WorkshopCompatRepair/original/3795496596/RelicRewardChoices/RelicRewardChoices.dll'),
 ('YuWanCard.Content','3747566068/lib/0.111.0/YuWanCard.Content.dll',['pig_regen'],H/'WorkshopCompatRepair/original/3747566068/lib/0.111.0/YuWanCard.Content.dll'),
 ('DixieRelicSort','3779056963/DixieRelicSort.dll',['dixie_hotkey'],H/'WorkshopCompatRepair/original/3779056963/DixieRelicSort.dll'),
 ('CardFusion','3747615796/CardFusion.dll',['fusion_enchants'],H/'FusionEnchantRepair/original/CardFusion.dll'),
 ('plague','3750672662/plague.dll',['plague_api'],H/'PlagueRepair/original/plague.dll'),
 ('Medo','3766908612/Medo.dll',['medo_events_api'],ROOT/'api/medo-columbina/original/Medo.dll'),
]
# 新旧原件分别生成差分，旧版支持继续保留。
for name,rel,options,digest in [
 ('AutoAnthony','3786611028/AutoAnthony.dll',['anthony_pool'],'EACE09DAF5DB7873BDC683AD8EF4BB6E223C58E118400CEC1B69D93384D0548D'),
 ('CardFusion','3747615796/CardFusion.dll',['fusion_enchants'],'10EB2C911D2591EB227308E2F041BB8A47A0D692B3CC3268DFAD4BA36B41F662'),
 ('CardFusion','3747615796/CardFusion.dll',['fusion_enchants'],'7B6FD1EDB04CF9D1735E1A680FC1CEF7BA9B0DD04D5D700CBC419C7C6D152B74')]:
    targets.append((name,rel,options,ROOT/'api/release-blockers/original'/digest/(name+'.dll')))
raw=ROOT/'delta-raw';raw.mkdir(exist_ok=True)
catalog=[]
for name,rel,options,original in targets:
    current=W/rel
    supported={sha(source) for n,_,_,source in targets if n==name}
    if not current.exists() or sha(current) not in supported:raise RuntimeError('Workshop changed: '+str(current))
    variants=[];data=original.read_bytes()
    for mask in range(1,2**len(options)):
        versioned=ROOT/'variants'/name/sha(original)[:12]
        fixed=(versioned if versioned.exists() else ROOT/'variants'/name)/str(mask)/(name+'.dll');new=fixed.read_bytes()
        delta=bytes((data[i] if i<len(data) else 0)^(new[i] if i<len(new) else 0) for i in range(max(len(data),len(new))))
        filename=f'{name}-{sha(original)[:12]}-{mask}.xor' if versioned.exists() else f'{name}-{mask}.xor'
        (raw/filename).write_bytes(delta)
        variants.append(dict(Mask=mask,Resource=filename+'.br',Length=len(new),Hash=sha(fixed)))
    catalog.append(dict(Name=name,OriginalHash=sha(original),Options=options,Variants=variants,WorkshopRelative=rel))
(ROOT/'catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf8')
print('CATALOG targets=',len(catalog),'variants=',sum(len(t['Variants']) for t in catalog))
