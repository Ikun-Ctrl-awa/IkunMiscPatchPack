"""建立原件校验清单与压缩差分；发行包不包含第三方原始资源包。"""
from pathlib import Path
import hashlib,json,subprocess
ROOT=Path(__file__).parent;H=ROOT.parent;W=Path('D:/Games/Steam/steamapps/workshop/content/2868840')
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
raw=ROOT/'delta-raw';raw.mkdir(exist_ok=True)
catalog=[]
for name,rel,options,original in targets:
    current=W/rel
    if not current.exists() or sha(current)!=sha(original):raise RuntimeError('Workshop changed: '+str(current))
    variants=[];data=original.read_bytes()
    for mask in range(1,2**len(options)):
        fixed=ROOT/'variants'/name/str(mask)/(name+'.dll');new=fixed.read_bytes()
        delta=bytes((data[i] if i<len(data) else 0)^(new[i] if i<len(new) else 0) for i in range(max(len(data),len(new))))
        filename=f'{name}-{mask}.xor';(raw/filename).write_bytes(delta)
        variants.append(dict(Mask=mask,Resource=filename+'.br',Length=len(new),Hash=sha(fixed)))
    catalog.append(dict(Name=name,OriginalHash=sha(original),Options=options,Variants=variants,WorkshopRelative=rel))
(ROOT/'catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf8')
print('CATALOG targets=',len(catalog),'variants=',sum(len(t['Variants']) for t in catalog))
